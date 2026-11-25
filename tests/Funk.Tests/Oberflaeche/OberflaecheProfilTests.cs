// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;
using Fehlercode = Funk.Protokoll.Steuerung.Fehlercode;
using SigniertesProfil = Funk.Protokoll.Identitaet.SigniertesProfil;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Serverprofil und Profilschlüssel im Bereich Verbindung (Modul profil): Einfügen und sofortiges Prüfen des Schlüssels,
/// Prüfung des Profils wie <c>funk-admin profil-pruefen</c>, "Verbinden erst, wenn beides passt" mit genauer Angabe, was
/// fehlt, der Rundlauf funk-admin -> Client (Profil mit <c>--host name/v4 --tcp-port 8443</c> geschrieben, signiert, im
/// Client gelesen und bis zu den Adressen des TLS-Aufbaus verfolgt), lesbare Fehlertexte und der Neuaufbau beim Verbinden.
/// Ohne Schannel, ohne Schlüsselspeicher im Profil, ohne Netz: flüchtige Schlüssel, Attrappen, erfundene Adressen.
/// </summary>
internal static class OberflaecheProfilTests
{
    /// <summary>Ein Profilschlüssel mit seinem öffentlichen Teil, wie funk-admin ihn ausgibt.</summary>
    internal sealed class Schluessel : IDisposable
    {
        public ECDsa Privat { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public string Spki => Convert.ToBase64String(Privat.ExportSubjectPublicKeyInfo());

        public string Kennung => Fingerabdruck.AusSpki(Privat.ExportSubjectPublicKeyInfo()).Anzeige;

        /// <summary>Die drei Zeilen von <c>funk-admin profilschluessel-erzeugen</c>.</summary>
        public string Ausgabe => "Profilschlüssel geschrieben. Öffentlicher Teil (SPKI, Base64) zum Einbau in den Client:\r\n" + Spki + "\r\nFingerabdruck: " + Kennung + "\r\n";

        public void Dispose() => Privat.Dispose();
    }

    /// <summary>Schreibt ein mit <paramref name="schluessel"/> signiertes Profil wie funk-admin (zwei Pins, ein Host).</summary>
    internal static void ProfilSchreiben(string pfad, Schluessel schluessel, string host = "funk.example.org", bool v4 = true, bool v6 = false,
        ushort port = 8443, DateTimeOffset? gueltigBis = null, bool bom = false)
    {
        using var pin = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = DateTimeOffset.UtcNow;
        var ende = gueltigBis ?? jetzt.AddDays(365);
        var profil = new Serverprofil("Meine Staffel", 1, Math.Min(jetzt.AddDays(-1).ToUnixTimeMilliseconds(), ende.AddDays(-1).ToUnixTimeMilliseconds()),
            ende.ToUnixTimeMilliseconds(), [new ProfilHost(host, v4, v6)], port, [Fingerabdruck.AusSchluessel(pin), Fingerabdruck.AusSchluessel(reserve)], []);
        var json = SigniertesProfil.Signieren(profil.AlsJson(), schluessel.Privat).AlsJson();
        File.WriteAllBytes(pfad, bom ? [0xEF, 0xBB, 0xBF, .. json] : json);
    }

    [Test]
    public static void Profilschluessel_EinfuegenPrueftSofort()
    {
        using var k = new Schluessel();
        void Gut(string text, string was)
        {
            var e = ProfilschluesselText.Pruefen(text);
            Pruefe.Wahr(e.Gueltig, was + ": " + e.Fehler);
            Pruefe.Gleich(k.Spki, e.Spki, was + ": SPKI in der Form der Verbindungsfabrik");
            Pruefe.Gleich(k.Kennung, e.Kennung, was + ": Kennung wie die Zeile Fingerabdruck von funk-admin");
            using var geladen = Profilschluessel.AusSpki(Convert.FromBase64String(e.Spki!));
            Pruefe.Gleich(k.Kennung, geladen.Fingerabdruck.Anzeige, was + ": Profilschluessel.AusSpki nimmt ihn");
        }
        void Schlecht(string text, string erwartet, string was)
        {
            var e = ProfilschluesselText.Pruefen(text);
            Pruefe.Falsch(e.Gueltig, was + ": abgelehnt");
            Pruefe.Wahr(e.Fehler is not null && e.Fehler.Contains(erwartet, StringComparison.Ordinal), was + ": " + e.Fehler);
            Pruefe.Falsch(e.Fehler!.Contains(k.Spki, StringComparison.Ordinal), was + ": die Meldung wiederholt die Eingabe nicht");
        }

        Pruefe.Gleich(SchluesselEingabe.Leer, ProfilschluesselText.Pruefen("   \r\n"), "leer: weder gültig noch Fehler");
        Pruefe.Wahr(k.Spki.StartsWith(ProfilschluesselText.Anfang, StringComparison.Ordinal) && k.Spki.Length == ProfilschluesselText.Laenge, "Form eines P-256-SPKI");
        Gut(k.Spki, "nur die Zeile");
        Gut(k.Ausgabe, "die ganze Ausgabe von funk-admin");
        Gut("  \"" + k.Spki + "\"  ", "mit Anführungszeichen und Leerraum");
        Gut(k.Spki[..70] + "\r\n" + k.Spki[70..], "vom Terminal umbrochen");
        Gut("-----BEGIN PUBLIC KEY-----\n" + k.Spki[..64] + "\n" + k.Spki[64..] + "\n-----END PUBLIC KEY-----\n", "als PEM");

        using var privat = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Schlecht(privat.ExportPkcs8PrivateKeyPem(), "private Profilschlüssel", "privater Schlüssel");
        Schlecht("Profilschlüssel geschrieben. Öffentlicher Teil (SPKI, Base64) zum Einbau in den Client:", "kein Profilschlüssel", "nur die Zeile davor");
        Schlecht(Fingerabdruck.AusSchluessel(privat).AlsBase64(), "Pin", "ein Pin aus dem Profil");
        Schlecht(k.Spki[..100], "100 von 124", "abgeschnitten");
        Schlecht(k.Spki + "AAAA", "zu lang", "zu viel kopiert");
        Schlecht("hallo", "kein Profilschlüssel", "irgendein Wort");
        Schlecht("MFkw-abc", "nur Buchstaben", "fremde Zeichen");
        using var rsa = RSA.Create(2048);
        Schlecht(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), "beginnt mit MFkw", "RSA statt P-256");
        var kaputt = k.Spki[..80] + (k.Spki[80] == 'A' ? 'B' : 'A') + k.Spki[81..];
        Schlecht(kaputt, "beschädigt", "ein Zeichen falsch");
        using var anderer = new Schluessel();
        Schlecht(k.Spki + "\nFingerabdruck: " + anderer.Kennung, "passen nicht zusammen", "Fingerabdruck eines anderen Schlüssels");
        Pruefe.Gleich(null, ProfilschluesselText.Kennung("MFkw"), "Kennung eines verdorbenen Eintrags");
    }

    [Test]
    public static void Profilpruefung_WieFunkAdminProfilPruefen()
    {
        using var o = new Testordner();
        using var k = new Schluessel();
        using var anderer = new Schluessel();
        var jetzt = DateTimeOffset.UtcNow;
        var profil = o.Datei("profil.json");
        ProfilSchreiben(profil, k);

        var b = Profilpruefung.Pruefen(null, [k.Spki], jetzt);
        Pruefe.Wahr(b.Unvollstaendig && !b.Gueltig && b.Fehler!.Contains("kein Serverprofil", StringComparison.Ordinal), "ohne Datei: " + b.Fehler);
        b = Profilpruefung.Pruefen(o.Datei("fehlt.json"), [k.Spki], jetzt);
        Pruefe.Wahr(b.Unvollstaendig && b.Fehler!.Contains("gibt es nicht", StringComparison.Ordinal) && b.Fehler.Contains("Reparieren", StringComparison.Ordinal),
            "Datei fehlt im Programmordner: offen, mit dem Weg: " + b.Fehler);
        var daten = o.Datei("daten.json");
        File.WriteAllBytes(daten, new Serverprofil("Meine Staffel", 1, 0, 1, [new ProfilHost("funk.example.org", true, false)], 8443,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []).AlsJson());
        b = Profilpruefung.Pruefen(daten, [k.Spki], jetzt);
        // Unsigniert: für Funker "nicht signiert" mit eigener Nummer; daten.json statt profil.json sagt die Nummer dem Admin (Q5).
        Pruefe.Wahr(b.Fehler!.Contains("nicht signiert", StringComparison.Ordinal) && b.Nummer == Fehlernummern.ProfilUnsigniert && b.Einzelheit == daten,
            "unsigniert: " + b.Fehler + " / " + b.Nummer + " / " + b.Einzelheit);
        var bedeutung = FehlernummernTabelle.Alle.Single(f => f.Nummer == b.Nummer).Bedeutung;
        Pruefe.Wahr(bedeutung.Contains("daten.json", StringComparison.Ordinal) && bedeutung.Contains("profil.json", StringComparison.Ordinal), "für den Admin: " + bedeutung);
        var muell = o.Datei("muell.json");
        File.WriteAllText(muell, "{\"daten\":\"AA==\"}");
        b = Profilpruefung.Pruefen(muell, [k.Spki], jetzt);
        Pruefe.Wahr(b.Fehler!.Contains("kein signiertes Serverprofil", StringComparison.Ordinal), "kein Profil: " + b.Fehler);

        b = Profilpruefung.Pruefen(profil, [], jetzt);
        Pruefe.Wahr(b.Unvollstaendig && b.Profil is null && b.Fehler!.Contains("Profilschlüssel", StringComparison.Ordinal), "ohne Schlüssel nichts gelesen: " + b.Fehler);
        b = Profilpruefung.Pruefen(profil, ["MFkw"], jetzt);
        Pruefe.Wahr(b.Unvollstaendig, "ein verdorbener Eintrag zählt nicht als Schlüssel");
        b = Profilpruefung.Pruefen(profil, [anderer.Spki], jetzt);
        Pruefe.Wahr(!b.Unvollstaendig && b.Profil is null && b.Fehler!.Contains("passt zu keinem", StringComparison.Ordinal), "fremder Schlüssel: " + b.Fehler);

        b = Profilpruefung.Pruefen(profil, [anderer.Spki, k.Spki], jetzt);
        Pruefe.Wahr(b.Gueltig && b.Fehler is null && !b.Abgelaufen, "passender Schlüssel an zweiter Stelle");
        Pruefe.Gleich(k.Kennung, b.Kennung, "welcher Schlüssel die Signatur trägt");
        Pruefe.Gleich(k.Spki, b.Schluessel, "sein SPKI");
        Pruefe.Gleich("funk.example.org (nur IPv4)", Profilpruefung.Hosts(b.Profil!), "Host mit Familie");
        Pruefe.Gleich((ushort)8443, b.Profil!.TcpPort, "öffentlicher Port aus dem Profil");

        var alt = o.Datei("alt.json");
        ProfilSchreiben(alt, k, gueltigBis: jetzt.AddDays(-3));
        b = Profilpruefung.Pruefen(alt, [k.Spki], jetzt);
        Pruefe.Wahr(b.Gueltig && b.Abgelaufen, "abgelaufen: gilt weiter, mit Warnung (3.1)");
        var mitBom = o.Datei("bom.json");
        ProfilSchreiben(mitBom, k, bom: true);
        Pruefe.Wahr(Profilpruefung.Pruefen(mitBom, [k.Spki], jetzt).Gueltig, "mit Byte-Reihenfolge-Marke eines Windows-Editors");
        var beide = o.Datei("beide.json");
        ProfilSchreiben(beide, k, v6: true);
        Pruefe.Gleich("funk.example.org (IPv4 und IPv6)", Profilpruefung.Hosts(Profilpruefung.Pruefen(beide, [k.Spki], jetzt).Profil!), "v4v6");
    }

    /// <summary>
    /// ANMELDUNG-SICHERHEIT P1: Profil und Profilschlüssel kommen mit dem Client (<see cref="Profilquelle"/>). Verbinden geht
    /// erst, wenn das eingebaute Profil zu einem eingebauten Schlüssel passt; sonst steht da, was fehlt, ohne Rot, solange die
    /// Fassung einfach keins mitbringt (dann nur Probefunk). Eine Eingabe von Profil oder Schlüssel gibt es nicht mehr.
    /// </summary>
    [Test]
    public static void Verbindung_EingebautesProfil_VerbindenErstWennEsPasst()
    {
        using var k = new Schluessel();
        using var fremd = new Schluessel();
        using var a = new Aufbau(FunkLage.Leer);
        var profil = a.Ordner.Datei("profil.json");
        ProfilSchreiben(profil, k);

        // Die Fassung des Testlaufs bringt nichts mit: offen, nicht rot, und nur der Probefunk geht.
        var ohne = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster);
        Pruefe.Falsch(ohne.VerbindenCommand.CanExecute(null), "ohne eingebautes Profil kein Verbinden");
        Pruefe.Wahr(ohne.ProfilFehler is null && ohne.ProfilOffen!.Contains("Probefunk", StringComparison.Ordinal), "offen, nicht rot: " + ohne.ProfilOffen);
        Pruefe.Wahr(ohne.ZeigtWasFehlt && ohne.WasFehlt!.StartsWith("Das Serverprofil gibt es nicht", StringComparison.Ordinal), "sagt, was fehlt: " + ohne.WasFehlt);
        Pruefe.Wahr(new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(null, [k.Spki])).ProfilOffen!.Contains("kein Serverprofil", StringComparison.Ordinal),
            "ohne Profildatei überhaupt: kein Serverprofil");

        // Profil da, aber keine Schlüssel: auch offen.
        var ohneSchluessel = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(profil, []));
        Pruefe.Wahr(!ohneSchluessel.Bereit && ohneSchluessel.ProfilOffen!.Contains("Profilschlüssel", StringComparison.Ordinal), "ohne Schlüssel: " + ohneSchluessel.ProfilOffen);

        // Falscher Schlüssel: Das Profil gehört nicht zu dieser Fassung; rot, mit dem Weg (Reparieren).
        var falsch = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(profil, [fremd.Spki]));
        Pruefe.Falsch(falsch.VerbindenCommand.CanExecute(null), "falscher Schlüssel: kein Verbinden");
        Pruefe.Wahr(falsch.ProfilFehler!.Contains("passt zu keinem", StringComparison.Ordinal) && falsch.ProfilFehler.Contains("Reparieren", StringComparison.Ordinal),
            "Profilfehler in der Karte: " + falsch.ProfilFehler);
        Pruefe.Wahr(falsch.WasFehlt!.StartsWith("Die Signatur des Serverprofils passt zu keinem eingebauten Profilschlüssel.", StringComparison.Ordinal),
            "oben kurz, was fehlt: " + falsch.WasFehlt);

        // Passt: Name, Host und Port aus dem Profil, Kennung des Schlüssels.
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(profil, [fremd.Spki, k.Spki]));
        Pruefe.Wahr(vm.Bereit && vm.VerbindenCommand.CanExecute(null) && !vm.ZeigtWasFehlt, "Profil und Schlüssel passen: " + vm.WasFehlt);
        Pruefe.Gleich("Meine Staffel: funk.example.org (nur IPv4), TCP-Port 8443", vm.ProfilZiel, "Name, Host und Port aus dem Profil");
        Pruefe.Wahr(vm.ProfilSignatur!.Contains("Signatur stimmt", StringComparison.Ordinal) && vm.ProfilSignatur.Contains(k.Kennung, StringComparison.Ordinal), vm.ProfilSignatur);
        Pruefe.Wahr(vm.ProfilFehler is null && vm.ProfilOffen is null, "kein Fehler, nichts offen");

        vm.Adresse = "https://funk.example.org";
        Pruefe.Wahr(!vm.VerbindenCommand.CanExecute(null) && vm.AdresseFehler is not null && vm.WasFehlt!.Contains("Adresse", StringComparison.Ordinal), "Adresse mit https: " + vm.WasFehlt);
        vm.Adresse = "192.168.1.250:8443";
        Pruefe.Wahr(vm.VerbindenCommand.CanExecute(null) && vm.AdresseFehler is null, "LAN-Adresse mit Port");

        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Verbinden });
        Pruefe.Falsch(vm.VerbindenCommand.CanExecute(null), "während des Verbindens nicht noch einmal");
        Pruefe.Falsch(vm.ZeigtWasFehlt, "und keine Liste, was fehlt");
        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });

        // Abgelaufen: Verbinden geht, aber mit Warnung (3.1).
        ProfilSchreiben(profil, k, gueltigBis: DateTimeOffset.UtcNow.AddDays(-1));
        vm.Pruefen();
        Pruefe.Wahr(vm.Bereit && vm.ProfilWarnung!.Contains("abgelaufen", StringComparison.Ordinal), "abgelaufen: " + vm.ProfilWarnung);

        // Vorführung und Testzugang: kein Profil nötig.
        using var c = new Aufbau(FunkLage.Leer);
        Pruefe.Wahr(new VerbindungViewModel(c.Dienst, c.Ablage, c.Takt, c.Fenster, ohneProfil: true).VerbindenCommand.CanExecute(null), "Vorführung");
        c.Ablage.Aktuell.Verbindung.Testzugang = "127.0.0.1:47000";
        Pruefe.Wahr(new VerbindungViewModel(c.Dienst, c.Ablage, c.Takt, c.Fenster).Bereit, "gespeicherter Testzugang (nur Loopback)");

        // Keine Eingabe mehr: weder Eigenschaften noch Befehle für Profil oder Profilschlüssel.
        var namen = typeof(VerbindungViewModel).GetMembers().Select(m => m.Name).ToList();
        Pruefe.Falsch(namen.Any(n => n.Contains("ProfilWaehlen", StringComparison.Ordinal) || n.Contains("NeuerSchluessel", StringComparison.Ordinal)
            || n.Contains("SchluesselHinzufuegen", StringComparison.Ordinal) || n == "ProfilSchluessel"), "keine Eingabe von Profil oder Schlüssel: " + string.Join(",", namen.Where(n => n.Contains("Schluessel", StringComparison.Ordinal))));
    }

    /// <summary>Die eingebauten Profilschlüssel aus den Metadaten der Programmdatei: nur gültige, jeder einmal, Trennzeichen egal.</summary>
    [Test]
    public static void Profilquelle_SchluesselAusDenMetadaten()
    {
        using var k = new Schluessel();
        using var r = new Schluessel();
        var name = new System.Reflection.AssemblyName("ProbeMetadaten" + Guid.NewGuid().ToString("N"));
        var programm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(name, System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var ctor = typeof(System.Reflection.AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        programm.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(ctor,
            [Profilquelle.Metadatenname, "# Profilschlüssel\r\n" + k.Spki + "\r\n" + r.Spki + ", " + k.Spki + "; MFkw kaputt\n"]));
        programm.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(ctor, ["Anderes", r.Spki]));
        var schluessel = Profilquelle.AusMetadaten(programm);
        Pruefe.Wahr(schluessel.SequenceEqual([k.Spki, r.Spki]), "zwei gültige, jeder einmal: " + schluessel.Count);
        // Seit dem 30.09.2026 liegt Sarevocs öffentlicher Profilschlüssel in konfig\profilschluessel.txt und kommt in jeden Bau, auch in
        // den des Testlaufs. Eingebaut ist genau das, was die Datei hergibt; ohne Datei nichts.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        var datei = Path.Combine(d?.FullName ?? throw new PruefungException("Repository nicht gefunden"), "konfig", "profilschluessel.txt");
        IReadOnlyList<string> erwartet = [];
        if (File.Exists(datei))
        {
            // Wie der Bau (Funk.Client.csproj): der ganze Text der Datei als Metadaten.
            var nachgebaut = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("ProbeKonfig" + Guid.NewGuid().ToString("N")),
                System.Reflection.Emit.AssemblyBuilderAccess.Run);
            nachgebaut.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(ctor, [Profilquelle.Metadatenname, File.ReadAllText(datei)]));
            erwartet = Profilquelle.AusMetadaten(nachgebaut);
        }
        var eingebaut = Profilquelle.AusMetadaten(typeof(Profilquelle).Assembly);
        Pruefe.Wahr(eingebaut.SequenceEqual(erwartet), $"eingebaut sind die Schlüssel aus konfig\\profilschluessel.txt: {eingebaut.Count} statt {erwartet.Count}");
        if (File.Exists(datei)) Pruefe.Wahr(eingebaut.Count >= 1, "mit der Datei mindestens ein Schlüssel");
        Pruefe.Gleich("profil.json", Path.GetFileName(Profilquelle.Eingebaut.Datei), "das Profil liegt im Programmordner");
        Pruefe.Gleich(Path.GetFullPath(AppContext.BaseDirectory), Path.GetFullPath(Path.GetDirectoryName(Profilquelle.Eingebaut.Datei)! + Path.DirectorySeparatorChar),
            "neben der Programmdatei");
    }

    [Test]
    public static void Verbindung_AenderungWaehrendDesVerbindens_SagtWannSieGilt()
    {
        // Befund der Gegenprüfung: Der Client bleibt beim Stand, mit dem er gebaut wurde. Trägt Sarevoc während "Verbinde..." die
        // LAN-Adresse ein (Anleitung, Abschnitt 10), ändert sich nichts, bis er trennt und neu verbindet; das muss dastehen.
        using var k = new Schluessel();
        using var a = new Aufbau(FunkLage.Leer);
        var profil = a.Ordner.Datei("profil.json");
        ProfilSchreiben(profil, k);
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(profil, [k.Spki]));
        Pruefe.Wahr(vm.Bereit, "bereit: " + vm.WasFehlt);

        vm.Adresse = "192.168.1.250";
        Pruefe.Gleich(null, vm.AenderungWartet, "getrennt: gilt ohnehin beim nächsten Verbinden");

        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Verbinden });
        vm.Adresse = "192.168.1.251";
        Pruefe.Wahr(vm.AenderungWartet is { } t && t.Contains("Trennen", StringComparison.Ordinal) && t.Contains("Verbinden", StringComparison.Ordinal),
            "während des Verbindens: " + vm.AenderungWartet);
        Pruefe.Falsch(vm.AenderungWartet!.Any(z => z is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen");
        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        Pruefe.Gleich(null, vm.AenderungWartet, "nach dem Trennen: Verbinden baut mit dem neuen Stand");

        // Die Adresse ebenso in jedem Zustand mit Client (den Gerätenamen ändert seit 0.6.0 niemand mehr in der Oberfläche, Q4).
        var n = 0;
        foreach (var status in new[] { Verbindungsstatus.Aktiv, Verbindungsstatus.Registrierung, Verbindungsstatus.Warteraum, Verbindungsstatus.AktivTunnel })
        {
            a.Dienst.LageSetzen(l => l with { Status = status });
            vm.Adresse = "192.168.1." + (10 + n++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Pruefe.Wahr(vm.AenderungWartet is not null, "Adresse bei " + status);
            a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
            Pruefe.Gleich(null, vm.AenderungWartet, "Adresse: nach dem Trennen weg");
        }
    }

    [Test]
    public static async Task Rundlauf_FunkAdminSchreibtWasDerClientErwartet()
    {
        using var o = new Testordner();
        // Genau die Schritte aus HEIMSERVER-PROXMOX.md, Abschnitt 6, mit funk-admin im selben Prozess.
        var (code, ausgabe) = await ServerPfadTests.Werkzeug("profilschluessel-erzeugen", "--schluessel", o.Datei("profil.key"));
        Pruefe.Gleich(0, code, "profilschluessel-erzeugen: " + ausgabe);
        var fingerabdruck = ausgabe.Split('\n', StringSplitOptions.TrimEntries).Single(z => z.StartsWith("Fingerabdruck: ", StringComparison.Ordinal))["Fingerabdruck: ".Length..];
        var pins = new List<string>();
        string text;
        foreach (var name in new[] { "server", "reserve" })
        {
            (code, text) = await ServerPfadTests.Werkzeug("schluessel-erzeugen", "--zertifikat", o.Datei(name + ".crt"), "--schluessel", o.Datei(name + ".key"));
            Pruefe.Gleich(0, code, "schluessel-erzeugen: " + text);
            pins.Add(text.Split('\n', StringSplitOptions.TrimEntries).Single(z => z.StartsWith("Pin (für pins im Serverprofil): ", StringComparison.Ordinal))["Pin (für pins im Serverprofil): ".Length..]);
        }
        (code, text) = await ServerPfadTests.Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "1", "--host", "funk.example.org/v4",
            "--tcp-port", "8443", "--pin", pins[0], "--pin", pins[1], "--aus", o.Datei("daten.json"));
        Pruefe.Gleich(0, code, "profil-schreiben: " + text);
        (code, text) = await ServerPfadTests.Werkzeug("profil-signieren", "--daten", o.Datei("daten.json"), "--schluessel", o.Datei("profil.key"), "--aus", o.Datei("profil.json"));
        Pruefe.Gleich(0, code, "profil-signieren: " + text);

        // Beim Bau des Clients: die ganze Ausgabe von funk-admin als konfig\profilschluessel.txt (Funk.Client.csproj); die
        // Profilquelle nimmt daraus genau den Schlüssel, dessen Kennung funk-admin nennt.
        var programm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("ProbeRundlauf" + Guid.NewGuid().ToString("N")),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        programm.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(System.Reflection.AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, [Profilquelle.Metadatenname, ausgabe]));
        var spki = Profilquelle.AusMetadaten(programm).Single();
        Pruefe.Gleich(fingerabdruck, ProfilschluesselText.Kennung(spki), "Kennung im Client = Fingerabdruck aus funk-admin");
        (code, text) = await ServerPfadTests.Werkzeug("profil-pruefen", "--profil", o.Datei("profil.json"), "--spki", spki);
        Pruefe.Wahr(code == 0 && text.Contains("Uebernehmen", StringComparison.Ordinal), "funk-admin profil-pruefen nimmt den eingebauten Schlüssel: " + text);

        // Legt das Setup versehentlich daten.json statt profil.json in den Programmordner, sagt der Client es.
        using var a = new Aufbau(FunkLage.Leer);
        var falsch = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, new Profilquelle(o.Datei("daten.json"), [spki]));
        Pruefe.Wahr(!falsch.Bereit && falsch.ProfilFehler!.Contains("nicht signiert", StringComparison.Ordinal), "daten.json statt profil.json: " + falsch.ProfilFehler);
        Pruefe.Gleich(Fehlernummern.ProfilUnsigniert, Profilpruefung.Waehlen(new Profilquelle(o.Datei("daten.json"), [spki]), DateTimeOffset.UtcNow).Nummer,
            "die Nummer sagt dem Admin: daten.json statt profil.json");
        var quelle = new Profilquelle(o.Datei("profil.json"), [spki]);
        var vm = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, quelle);
        Pruefe.Wahr(vm.Bereit && vm.VerbindenCommand.CanExecute(null), "bereit: " + vm.WasFehlt);
        Pruefe.Gleich("Meine Staffel: funk.example.org (nur IPv4), TCP-Port 8443", vm.ProfilZiel, "was funk-admin schrieb, zeigt der Client");

        // Die Verbindungsfabrik baut daraus den Client, mit flüchtigem Schlüssel statt dem Speicher von Windows.
        var anbindung = Verbindungsfabrik.Bauen(a.Ablage.Aktuell.Verbindung, null, speicher: new FluechtigerSchluesselspeicher(), profil: quelle);
        try
        {
            Pruefe.Gleich("Meine Staffel", anbindung.Server, "Server aus dem Profil");
            Pruefe.Gleich("Software", anbindung.Schluesselart, "Schlüsselart für die Anzeige");
        }
        finally
        {
            await anbindung.Client.DisposeAsync();
            anbindung.Besitz?.Dispose();
        }

        // Bis zu den Adressen: nur A-Einträge, nur IPv4, Port 8443, SNI der Name aus dem Profil.
        Pruefe.Wahr(SigniertesProfil.TryLesen(File.ReadAllBytes(o.Datei("profil.json")), out var angebot), "profil.json lesbar");
        Pruefe.Wahr(Serverprofil.TryLesen(angebot!.Daten, out var profil), "Profildaten gültig");
        var gefragt = new List<(string, AddressFamily)>();
        using var id = Geraeteidentitaet.Fluechtig();
        var optionen = Verbindungsfabrik.AufbauOptionen(a.Ablage.Aktuell.Verbindung, profil!);
        var aufbau = new TlsAufbau(profil!, id, new TlsAufbauOptionen
        {
            Host = optionen.Host,
            Port = optionen.Port,
            Aufloeser = (name, familie, _) =>
            {
                gefragt.Add((name, familie));
                return Task.FromResult<IPAddress[]>([IPAddress.Parse("2001:db8::7"), IPAddress.Parse("203.0.113.7")]);
            },
        });
        Pruefe.Gleich((ushort)8443, aufbau.Port, "öffentlicher Port 8443 aus dem Profil");
        var kandidaten = await aufbau.KandidatenAsync(CancellationToken.None);
        Pruefe.Gleich(("funk.example.org", AddressFamily.InterNetwork), gefragt.Single(), "/v4: nur nach IPv4 gefragt");
        Pruefe.Gleich((IPAddress.Parse("203.0.113.7"), "funk.example.org"), kandidaten.Single(), "nur die IPv4-Adresse, SNI aus dem Profil");

        // Die LAN-Adresse aus dem Feld Adresse ersetzt nur den Host; SNI und Pin bleiben die des Profils.
        a.Ablage.Aktuell.Verbindung.Adresse = "192.168.1.250";
        optionen = Verbindungsfabrik.AufbauOptionen(a.Ablage.Aktuell.Verbindung, profil!);
        var lan = new TlsAufbau(profil!, id, optionen);
        Pruefe.Gleich((IPAddress.Parse("192.168.1.250"), "funk.example.org"), (await lan.KandidatenAsync(CancellationToken.None)).Single(), "LAN-Adresse");
        Pruefe.Gleich((ushort)8443, lan.Port, "Port bleibt der aus dem Profil");
        a.Ablage.Aktuell.Verbindung.Adresse = "https://funk.example.org";
        var e = Pruefe.Wirft<VorbereitungException>(() => Verbindungsfabrik.AufbauOptionen(a.Ablage.Aktuell.Verbindung, profil!), "Adresse mit https");
        Pruefe.Wahr(e.Meldung.Einzelheit is { } grund && grund.Contains("ohne http", StringComparison.Ordinal), "der Grund für den Admin: " + e.Meldung.Einzelheit);
        Pruefe.Falsch(e.Message.Contains("funk.example.org", StringComparison.Ordinal), "der Satz für Funker nennt die Adresse nicht: " + e.Message);
    }

    [Test]
    public static async Task FunkAdmin_ProfilSchreiben_SagtGenauWasAmHostFalschIst()
    {
        using var o = new Testordner();
        var pin = Fingerabdruck.AusSpki([1]).AlsBase64();
        var reserve = Fingerabdruck.AusSpki([2]).AlsBase64();
        async Task Falsch(string host, string port, string erwartet)
        {
            var (code, text) = await ServerPfadTests.Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "1", "--host", host,
                "--tcp-port", port, "--pin", pin, "--pin", reserve, "--aus", o.Datei("daten.json"));
            Pruefe.Gleich(2, code, host + " " + port + ": Aufruffehler");
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal), host + " " + port + ": " + text);
        }
        await Falsch("https://funk.example.org/v4", "8443", "ohne https://");
        await Falsch("funk.example.org:8443/v4", "8443", "ohne Port");
        await Falsch("funk.example.org/ipv4", "8443", "/v4, /v6 oder /v4v6");
        await Falsch("funk_example.org/v4", "8443", "kein gültiger Name");
        await Falsch("funk.example.org/v4", "70000", "zwischen 1 und 65535");
        await Falsch("funk.example.org/v4", "0", "zwischen 1 und 65535");
        var (ok, ausgabe) = await ServerPfadTests.Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "1", "--host", "funk.example.org",
            "--tcp-port", "8443", "--pin", pin, "--pin", reserve, "--aus", o.Datei("daten.json"));
        Pruefe.Gleich(0, ok, "ohne Familie gilt v4: " + ausgabe);
        Pruefe.Wahr(Serverprofil.TryLesen(File.ReadAllBytes(o.Datei("daten.json")), out var p) && p.Hosts.Single() is { IPv4: true, IPv6: false }, "nur IPv4");
    }

    [Test]
    public static void Adressangabe_NameIpUndPort()
    {
        void Gut(string text, string host, ushort? port)
        {
            Pruefe.Wahr(Adressangabe.TryLesen(text, out var a, out var f), text + ": " + f);
            Pruefe.Gleich(new Adressangabe(host, port), a, text);
        }
        void Schlecht(string text, string erwartet)
        {
            Pruefe.Falsch(Adressangabe.TryLesen(text, out _, out var f), text + ": abgelehnt");
            Pruefe.Wahr(f!.Contains(erwartet, StringComparison.Ordinal), text + ": " + f);
        }
        Pruefe.Wahr(Adressangabe.TryLesen("  ", out var leer, out _) && leer is null, "leer: Hosts aus dem Profil");
        Gut("funk.example.org", "funk.example.org", null);
        Gut(" 192.168.1.250 ", "192.168.1.250", null);
        Gut("192.168.1.250:8443", "192.168.1.250", 8443);
        Gut("funk.example.org:9443", "funk.example.org", 9443);
        Gut("2001:db8::5", "2001:db8::5", null);
        Gut("[2001:db8::5]:8443", "2001:db8::5", 8443);
        Schlecht("https://funk.example.org", "ohne http");
        Schlecht("funk.example.org/profil", "Zeichen");
        Schlecht("funk example", "Zeichen");
        Schlecht("funk.example.org:0", "1 bis 65535");
        Schlecht("funk.example.org:acht", "1 bis 65535");
        Schlecht("[2001:db8::5", "Klammer");
        Schlecht("funk_example", "weder");
        Schlecht("1", "weder");
        Schlecht("192.168.178", "weder");
    }

    [Test]
    public static void Registrierung_CodeUndFreigabeWieVomServer()
    {
        using var a = new Aufbau(FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            // So schickt es der Server (Zentrale.Registrierungsbefehl): der Befehl ohne Code.
            Registrierung = new Registrierungsangabe("ABCD-EFGH-JKMN", "/funk verknuepfen", DateTimeOffset.UtcNow.AddMinutes(5)),
        });
        var h = a.Haupt(out _, out _, out _, out _);
        var vm = h.Verbindung;
        Pruefe.Wahr(vm.IstRegistrierung, "Karte sichtbar");
        Pruefe.Gleich("ABCD-EFGH-JKMN", vm.Code, "der Code selbst steht da");
        Pruefe.Gleich("/funk verknuepfen ABCD-EFGH-JKMN", vm.CodeBefehl, "der ganze Befehl für Discord, mit Code");

        // Der Code holt den Bereich Verbindung nach vorn, aber nur beim Erscheinen.
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        h.AktiverBereich = Seiten.Einstellungen(h);
        a.Dienst.LageSetzen(_ => FunkLage.Leer with
        {
            Status = Verbindungsstatus.Registrierung,
            Registrierung = new Registrierungsangabe("ABCD-EFGH-JKMN", "/funk verknuepfen", DateTimeOffset.UtcNow.AddMinutes(5)),
        });
        Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "Code erschienen: Startseite");
        h.AktiverBereich = Seiten.Einstellungen(h);
        a.Dienst.LageSetzen(l => l with { Hinweis = "noch etwas" });
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "danach darf Sarevoc woanders hinschauen");

        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Warteraum, Geraet = "1A2B-3C4D-5E6F-7A8B", Schluesselart = "TPM" });
        Pruefe.Wahr(vm.IstWarteraum && vm.GeraetAnzeige == "1A2B-3C4D-5E6F-7A8B", "Anzeigeform zum Freigeben, ohne Schlüsselart");
        Pruefe.Gleich("1A2B-3C4D-5E6F-7A8B (TPM)", vm.Geraet, "in der Übersicht mit Schlüsselart");
        Pruefe.Falsch(vm.IstRegistrierung, "Code weg");
    }

    [Test]
    public static void Verbindungstexte_LesbarStattCodes()
    {
        Verbindungsmeldung Meldung(string grund, ByeGrund? bye = null, Fehlercode? fehler = null, AufbauFehler? art = null, string? einzelheit = null, uint? wieder = null,
            ByeDurch? durch = null) => Verbindungstexte.Ende(new Sitzungsende(grund, bye, wieder, fehler, null, durch, art, einzelheit));
        string Ende(string grund, ByeGrund? bye = null, Fehlercode? fehler = null, AufbauFehler? art = null, string? einzelheit = null, uint? wieder = null, ByeDurch? durch = null) =>
            Meldung(grund, bye, fehler, art, einzelheit, wieder, durch).Text;
        var texte = new Dictionary<string, string>
        {
            ["netz"] = Ende("Aufbau: Netz", art: AufbauFehler.Netz, einzelheit: "funk.example.org hat keine IPv4-Adresse"),
            ["tcp"] = Ende("Aufbau: Netz", art: AufbauFehler.Netz, einzelheit: "203.0.113.7:8443 antwortet nicht"),
            ["tls"] = Ende("Aufbau: Tls", art: AufbauFehler.Tls, einzelheit: "TLS mit 203.0.113.7:8443 gescheitert: x"),
            ["alpn"] = Ende("Aufbau: Alpn", art: AufbauFehler.Alpn, einzelheit: "203.0.113.7:8443 spricht nicht tacrelay/1 (ALPN)"),
            ["pin"] = Ende("Aufbau: Pin", art: AufbauFehler.Pin),
            ["alarm"] = Ende("Aufbau: PinAlarm", art: AufbauFehler.PinAlarm, einzelheit: "Pin passt nicht an 203.0.113.7:8443"),
            ["wartung"] = Ende("Bye Wartung", bye: ByeGrund.Wartung, wieder: 10_000),
            ["verdraengt"] = Ende("Bye Verdraengt", bye: ByeGrund.Verdraengt, durch: new ByeDurch { GeraetName = "Laptop", Anzeige = "1A2B-3C4D-5E6F-7A8B" }),
            ["alt"] = Ende("Fehler VERSION_ZU_ALT", fehler: Fehlercode.VersionZuAlt),
            ["ping"] = Ende("6 s ohne Ping-Antwort"),
            ["selbst"] = Ende("Beendet", bye: ByeGrund.Beendet),
            ["server"] = Ende("Bye Beendet", bye: ByeGrund.Beendet),
        };
        // Funker lesen die Lage und was sie tun können; Host, Adresse und Port gehen als Einzelheit an die Details (Q5, Q14).
        Pruefe.Wahr(texte["netz"].StartsWith("Server nicht erreichbar.", StringComparison.Ordinal) && texte["netz"].Contains(Verbindungstexte.NeuerVersuch, StringComparison.Ordinal)
            && !texte["netz"].Contains("funk.example.org", StringComparison.Ordinal), texte["netz"]);
        Pruefe.Gleich("funk.example.org hat keine IPv4-Adresse", Meldung("Aufbau: Netz", art: AufbauFehler.Netz, einzelheit: "funk.example.org hat keine IPv4-Adresse").Einzelheit,
            "Host als Einzelheit");
        Pruefe.Wahr(!texte["tcp"].Contains("203.0.113.7", StringComparison.Ordinal) && texte["tcp"].Contains("Internetverbindung", StringComparison.Ordinal), texte["tcp"]);
        Pruefe.Gleich("203.0.113.7:8443 antwortet nicht", Meldung("Aufbau: Netz", art: AufbauFehler.Netz, einzelheit: "203.0.113.7:8443 antwortet nicht").Einzelheit,
            "Adresse:Port als Einzelheit");
        Pruefe.Wahr(texte["tls"].Contains("sichere Verbindungsaufbau", StringComparison.Ordinal) && !texte["tls"].Contains("203.0.113.7", StringComparison.Ordinal), texte["tls"]);
        Pruefe.Wahr(texte["alpn"].Contains("kein JG-TacRelay-Server", StringComparison.Ordinal), texte["alpn"]);
        Pruefe.Wahr(texte["pin"].Contains("60 s", StringComparison.Ordinal), texte["pin"]);
        Pruefe.Wahr(texte["alarm"].StartsWith("Server nicht vertrauenswürdig", StringComparison.Ordinal) && !texte["alarm"].Contains("von selbst", StringComparison.Ordinal),
            "Alarm ohne Neuversuch: " + texte["alarm"]);
        Pruefe.Gleich(1, texte["alarm"].Split("vertrauenswürdig").Length - 1, "einmal gesagt reicht: " + texte["alarm"]);
        Pruefe.Wahr(texte["wartung"].Contains("10 s", StringComparison.Ordinal), texte["wartung"]);
        // Das andere Gerät (Windows-Gerätename, Kennung) nur als Einzelheit für Details (Q4; Gegenprüfung Stufe 2).
        Pruefe.Wahr(!texte["verdraengt"].Contains("Laptop", StringComparison.Ordinal) && !texte["verdraengt"].Contains("1A2B-3C4D-5E6F-7A8B", StringComparison.Ordinal), texte["verdraengt"]);
        Pruefe.Wahr(Meldung("Bye Verdraengt", bye: ByeGrund.Verdraengt, durch: new ByeDurch { GeraetName = "Laptop", Anzeige = "1A2B-3C4D-5E6F-7A8B" }).Einzelheit
            is { } anderes && anderes.Contains("Laptop", StringComparison.Ordinal) && anderes.Contains("1A2B-3C4D-5E6F-7A8B", StringComparison.Ordinal), "anderes Gerät als Einzelheit");
        Pruefe.Wahr(texte["alt"].Contains("zu alt", StringComparison.Ordinal), texte["alt"]);
        Pruefe.Wahr(texte["ping"].Contains("6 s", StringComparison.Ordinal), texte["ping"]);
        Pruefe.Gleich("Getrennt.", texte["selbst"], "selbst getrennt");
        Pruefe.Wahr(texte["server"].Contains("Server hat die Verbindung beendet", StringComparison.Ordinal), texte["server"]);
        foreach (var (name, t) in texte)
        {
            Pruefe.Falsch(t.Contains("Aufbau:", StringComparison.Ordinal) || t.Contains("PinAlarm", StringComparison.Ordinal), name + ": keine Codes: " + t);
            Pruefe.Falsch(t.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), name + ": nur einfache Zeichen: " + t);
        }
    }

    [Test]
    public static async Task Dienst_ZeigtVersucheUndBautBeimVerbindenNeu()
    {
        Pruefe.Gleich(Verbindungsstatus.Verbinden, FunkClientDienst.AnzeigeStatus(ClientZustand.Getrennt, neuversuchGesperrt: false), "zwischen zwei Versuchen");
        Pruefe.Gleich(Verbindungsstatus.Getrennt, FunkClientDienst.AnzeigeStatus(ClientZustand.Getrennt, neuversuchGesperrt: true), "kein Neuversuch mehr");
        Pruefe.Gleich(Verbindungsstatus.Registrierung, FunkClientDienst.AnzeigeStatus(ClientZustand.Registrierung, false), "sonst wie bisher");

        // Erstverbindung mit falschem Pin: "Server nicht vertrauenswürdig", Getrennt, kein stilles Weiterprobieren.
        var gebaut = 0;
        var art = AufbauFehler.PinAlarm;
        await using var d = new FunkClientDienst(() =>
        {
            gebaut++;
            var id = Geraeteidentitaet.Fluechtig();
            var aufbauArt = art;
            return new FunkAnbindung(new FunkClient(new FunkClientOptionen
            {
                Aufbau = new DelegatAufbau((_, _) => throw (aufbauArt == AufbauFehler.Tls
                    ? new InvalidOperationException("kaputt")
                    : (Exception)new AufbauException(aufbauArt, "Pin passt nicht an 203.0.113.7:8443"))),
                Identitaet = id,
                Zufall = () => 0.0,
            }), id, "Meine Staffel", id.Fingerabdruck.Anzeige, "Software");
        });
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Hinweis is { } h && h.Contains("vertrauenswürdig", StringComparison.Ordinal), 5_000), "Alarm angezeigt: " + d.Lage.Hinweis);
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Getrennt, 2_000), "Getrennt, der Knopf Verbinden geht wieder: " + d.Lage.Status);

        // Verbinden baut neu, mit dem aktuellen Stand der Einstellungen.
        art = AufbauFehler.Netz;
        await d.VerbindenAsync();
        Pruefe.Gleich(2, gebaut, "neu gebaut statt des alten Clients");
        Pruefe.Wahr(await Warten(() => d.Lage.Hinweis is { } h && h.StartsWith("Server nicht erreichbar", StringComparison.Ordinal), 5_000), "lesbar: " + d.Lage.Hinweis);
        Pruefe.Gleich(Verbindungsstatus.Verbinden, d.Lage.Status, "während der Neuversuche: Verbinde...");

        // Unerwartete Ausnahme in der Verbindungsschleife: steht da, statt still zu hängen.
        art = AufbauFehler.Tls;
        await d.TrennenAsync();
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.HinweisEinzelheit is { } e && e.Contains("kaputt", StringComparison.Ordinal), 5_000), "Hintergrundfehler: " + d.Lage.HinweisEinzelheit);
        Pruefe.Wahr(d.Lage.Hinweis is { } h && h.StartsWith("Interner Fehler", StringComparison.Ordinal) && !h.Contains("kaputt", StringComparison.Ordinal),
            "der Satz ohne Ausnahmetext: " + d.Lage.Hinweis);
        await d.TrennenAsync();
        Pruefe.Gleich(Verbindungsstatus.Getrennt, d.Lage.Status, "getrennt");
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
}
