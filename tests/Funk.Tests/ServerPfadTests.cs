// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Pfadprüfung und Adressbindung (5.10), Fristen (4.6), geordnetes Beenden (7.2), Sperre ohne Antwort (P-12),
/// Messwerte (7.8) und das Admin-Werkzeug als Kommandozeile (3.8).
/// </summary>
internal static class ServerPfadTests
{
    private static readonly byte[] Opus = [0xF8, 0x01, 0x02];

    private static JsonObject Netz(ushort id) => new()
    {
        ["objekt"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = "Netz " + id,
            ["typ"] = "NORMAL",
            ["reihenfolge"] = id,
            ["nurTaste"] = false,
            ["ausloeserStandard"] = "TASTE",
            ["offen"] = "HOEREN_SENDEN",
            ["standard"] = true,
        },
    };

    [Test]
    public static async Task Pfad_NatRebindingMitPruefungUndGegenprobe()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2));
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        await using var b = await t.AktivAsync("222222222222222222", "bert");
        a.UdpVerbinden();
        b.UdpVerbinden();
        await a.UdpBindenAsync();
        await b.UdpBindenAsync();

        // Das NAT wechselt den Port: gültige Pakete kommen jetzt von A, B verstummt.
        a.UdpPortWechseln();
        var klartext = new byte[Steuerpakete.Laenge];
        new Keepalive(1, 10).Schreiben(klartext);
        a.UdpSenden(PaketTyp.Keepalive, klartext);
        Pruefe.Wahr(await a.PfadPruefungBeantwortenAsync(TimeSpan.FromSeconds(2)), "PFAD_PRUEFUNG an die neue Adresse (ausgelöst durch ein gültiges Paket)");
        var status = (await a.ErwarteAsync(Nachrichtentyp.UdpStatus, TimeSpan.FromSeconds(3))).Json<UdpStatus>();
        Pruefe.Gleich(UdpZustand.Gebunden, status.Zustand, "umgebunden nach 500 ms Schweigen von B");
        Pruefe.Gleich(true, status.GleicheIp, "gleiche IP wie die TLS-Verbindung");

        // Runter-Sprache geht jetzt an die neue Adresse.
        b.SpracheSenden([2], Opus, frame: 1, phase: 1, kz: Kennzeichen.Beginn);
        var r = await a.UdpEmpfangenAsync(TimeSpan.FromSeconds(2));
        Pruefe.Wahr(r is not null && r.Value.Kopf.Typ == PaketTyp.SpracheRunter, "Sprache kommt an der neuen Adresse an");
    }

    [Test]
    public static async Task Pfad_UngueltigesVonFremderAdresseLoestNichtsAus()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        a.UdpVerbinden();
        await a.UdpBindenAsync();

        // Ein blinder Angreifer kennt die Sitzungs-ID, aber keinen Schlüssel: keine Prüfung, keine Antwort (5.10, P-07).
        using var angreifer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows()) angreifer.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        angreifer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        angreifer.Connect(t.Dienst.UdpEndpunkte[0]);
        var d = new byte[48];
        for (var i = 0; i < 50; i++)
        {
            RandomNumberGenerator.Fill(d);
            d[0] = 1;
            d[1] = (byte)PaketTyp.Keepalive;
            d[2] = 0;
            d[3] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(4), a.SitzungsId);
            BinaryPrimitives.WriteUInt64BigEndian(d.AsSpan(8), 1_000_000UL + (ulong)i);
            angreifer.Send(d);
        }
        // Eine Kopie eines echten, schon angenommenen Pakets von fremder Adresse ist ein Replay: ebenfalls nichts.
        a.Versiegeln(PaketTyp.Keepalive, new byte[Steuerpakete.Laenge], out var echt);
        a.RohUdpSenden(echt);
        angreifer.Send(echt);
        using var abbruch = new CancellationTokenSource(500);
        var bekommen = false;
        try
        {
            await angreifer.ReceiveAsync(new byte[2048], SocketFlags.None, abbruch.Token);
            bekommen = true;
        }
        catch (OperationCanceledException)
        {
        }
        Pruefe.Falsch(bekommen, "keine PFAD_PRUEFUNG an den Angreifer");
        await Task.Delay(700);
        Pruefe.Falsch(a.Hat(r => r.Typ == Nachrichtentyp.UdpStatus), "die Bindung bleibt, kein neuer UdpStatus");
    }

    [Test]
    public static async Task Frist_OhneHelloNachDreiSekundenGetrennt()
    {
        await using var t = await Testserver.StartenAsync();
        await using var f = await t.VerbindenAsync();
        var start = DateTime.UtcNow;
        await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(6));
        var dauer = DateTime.UtcNow - start;
        Pruefe.Wahr(dauer >= TimeSpan.FromSeconds(2.5), "nicht vor der Hello-Frist (" + dauer.TotalMilliseconds + " ms)");
        Pruefe.Wahr(t.Dienst.Zentrale.Zulassung.UnbekannteVerbindungen == 0 || await WartenAufNull(t), "Platz für Unfreigegebene wieder frei");
    }

    private static async Task<bool> WartenAufNull(Testserver t)
    {
        for (var i = 0; i < 50; i++)
        {
            if (t.Dienst.Zentrale.Zulassung.UnbekannteVerbindungen == 0) return true;
            await Task.Delay(20);
        }
        return false;
    }

    [Test]
    public static async Task Wartung_GeordnetesBeendenSendetBye()
    {
        var t = await Testserver.StartenAsync();
        try
        {
            await using var a = await t.AktivAsync("111111111111111111", "anna");
            await t.GeordnetBeendenAsync(5000);
            var bye = (await a.ErwarteAsync(Nachrichtentyp.Bye)).Json<Bye>();
            Pruefe.Gleich(ByeGrund.Wartung, bye.Grund, "Bye WARTUNG");
            Pruefe.Gleich(5000u, bye.WiederInMs, "wiederInMs");
            await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await t.DisposeAsync();
        }
    }

    [Test]
    public static async Task Sperre_DanachBleibtUdpOhneAntwort()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        a.UdpVerbinden();
        await a.UdpBindenAsync();
        var ka = new byte[Steuerpakete.Laenge];
        new Keepalive(1, 1).Schreiben(ka);
        a.UdpSenden(PaketTyp.Keepalive, ka);
        Pruefe.Wahr(await a.UdpEmpfangenAsync(TimeSpan.FromSeconds(2)) is not null, "Echo vor der Sperre");
        await t.AdminOk("konto-sperren", new JsonObject { ["konto"] = a.KontoId });
        await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(200);
        new Keepalive(2, 2).Schreiben(ka);
        a.UdpSenden(PaketTyp.Keepalive, ka);
        Pruefe.Wahr(await a.UdpEmpfangenAsync(TimeSpan.FromMilliseconds(500)) is null, "Schlüssel gelöscht, keine Antwort mehr (P-12)");
    }

    [Test]
    public static async Task Messwerte_JeSitzungUeberDasAdminWerkzeug()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2));
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        await using var b = await t.AktivAsync("222222222222222222", "bert");
        a.UdpVerbinden();
        b.UdpVerbinden();
        await a.UdpBindenAsync();
        await b.UdpBindenAsync();
        for (uint i = 0; i < 5; i++) a.SpracheSenden([2], Opus, 100 + i, 3, i == 0 ? Kennzeichen.Beginn : Kennzeichen.None);
        await b.UdpEmpfangenAsync(TimeSpan.FromSeconds(1));
        var m = await t.AdminOk("messwerte");
        Pruefe.Wahr(m.Text.Contains("2 Sitzungen", StringComparison.Ordinal), "zwei Sitzungen: " + m.Text);
        var je = m.Daten!["jeSitzung"]!.AsArray();
        Pruefe.Gleich(2, je.Count, "Messwerte je Sitzung");
        Pruefe.Wahr(je.All(x => x!["gebunden"]!.GetValue<bool>()), "beide gebunden");
        var sicherung = await t.AdminOk("sicherung");
        Pruefe.Wahr(sicherung.Daten!["dateien"]!.AsArray().Count >= 2, "Sicherung enthält Lage und Konten");
    }

    [Test]
    public static async Task Sprechanzeige_NurFuerAbonnentenMitRecht()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2));
        await using var leitung = await t.AktivAsync("111111111111111111", "anna", "RAIDLEITER");
        await using var funker = await t.AktivAsync("222222222222222222", "bert");
        await using var sprecher = await t.AktivAsync("333333333333333333", "carl");
        sprecher.UdpVerbinden();
        await sprecher.UdpBindenAsync();

        var ohneRecht = await funker.AnfrageAsync(Nachrichtentyp.SprechanzeigeAbo, new AnAus { An = true });
        Pruefe.Gleich(Fehlercode.KeinRecht, ohneRecht.Json<Fehler>().Code, "Funker ohne SPRECHANZEIGE_ALLE");
        var abo = await leitung.AnfrageAsync(Nachrichtentyp.SprechanzeigeAbo, new AnAus { An = true });
        Pruefe.Gleich(Nachrichtentyp.SprechanzeigeAbo, abo.Typ, "Abo angenommen");

        sprecher.SpracheSenden([2], Opus, frame: 1, phase: 5, kz: Kennzeichen.Beginn);
        var an = (await leitung.ErwarteAsync(Nachrichtentyp.Sprechanzeige, TimeSpan.FromSeconds(2))).Json<Sprechanzeige>();
        Pruefe.Gleich(1, an.Aktiv.Count, "ein Sprecher");
        Pruefe.Gleich(sprecher.KontoId, an.Aktiv[0].KontoId, "Konto des Sprechers");
        Pruefe.Gleich(SprechArt.Netz, an.Aktiv[0].Art, "Art NETZ");
        Pruefe.Gleich((ushort)2, an.Aktiv[0].Netze[0], "Netz");
        sprecher.SpracheSenden([2], [], frame: 2, phase: 5, kz: Kennzeichen.Ende);
        var aus = (await leitung.ErwarteAsync(Nachrichtentyp.Sprechanzeige, TimeSpan.FromSeconds(2))).Json<Sprechanzeige>();
        Pruefe.Gleich(0, aus.Aktiv.Count, "nach ENDE spricht niemand");
        Pruefe.Falsch(funker.Hat(r => r.Typ == Nachrichtentyp.Sprechanzeige), "ohne Abo keine Sprechanzeige");
    }

    [Test]
    public static async Task Werkzeug_SchluesselProfilUndStatus()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "werkzeug-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(verzeichnis);
        try
        {
            var (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", Path.Combine(verzeichnis, "server.crt"), "--schluessel", Path.Combine(verzeichnis, "server.key"));
            Pruefe.Gleich(0, code, "schluessel-erzeugen: " + text);
            var pin = Zeile(text, "Pin (für pins im Serverprofil): ");
            (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", Path.Combine(verzeichnis, "reserve.crt"), "--schluessel", Path.Combine(verzeichnis, "reserve.key"));
            var reserve = Zeile(text, "Pin (für pins im Serverprofil): ");
            (code, text) = await Werkzeug("profilschluessel-erzeugen", "--schluessel", Path.Combine(verzeichnis, "profil.key"));
            Pruefe.Gleich(0, code, "profilschluessel-erzeugen");
            var spki = text.Split('\n', StringSplitOptions.TrimEntries)[1];
            (code, text) = await Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "3", "--host", "funk.example.org/v4",
                "--tcp-port", "443", "--pin", pin, "--pin", reserve, "--aus", Path.Combine(verzeichnis, "daten.json"));
            Pruefe.Gleich(0, code, "profil-schreiben: " + text);
            (code, text) = await Werkzeug("profil-signieren", "--daten", Path.Combine(verzeichnis, "daten.json"), "--schluessel", Path.Combine(verzeichnis, "profil.key"),
                "--aus", Path.Combine(verzeichnis, "profil.json"));
            Pruefe.Gleich(0, code, "profil-signieren: " + text);
            (code, text) = await Werkzeug("profil-pruefen", "--profil", Path.Combine(verzeichnis, "profil.json"), "--spki", spki);
            Pruefe.Gleich(0, code, "profil-pruefen: " + text);
            Pruefe.Wahr(text.Contains("Uebernehmen", StringComparison.Ordinal) && text.Contains(pin, StringComparison.Ordinal), "Profil gilt und nennt den Pin");
            using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            (code, _) = await Werkzeug("profil-pruefen", "--profil", Path.Combine(verzeichnis, "profil.json"), "--spki", Convert.ToBase64String(fremd.ExportSubjectPublicKeyInfo()));
            Pruefe.Gleich(1, code, "fremder Profilschlüssel: Signatur ungültig");
            Pruefe.Falsch(File.ReadAllText(Path.Combine(verzeichnis, "server.crt")).Contains("PRIVATE", StringComparison.Ordinal), "Zertifikat ohne Schlüssel");
            using (var paar = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                Path.Combine(verzeichnis, "server.crt"), Path.Combine(verzeichnis, "server.key")))
            {
                Pruefe.Wahr(paar.HasPrivateKey, "Schlüssel (PKCS#8-PEM) passt zum Zertifikat");
                Pruefe.Gleich(pin, Funk.Protokoll.Identitaet.Fingerabdruck.AusZertifikat(paar).AlsBase64(), "ausgegebener Pin ist der des Zertifikats");
            }

            await using var t = await Testserver.StartenAsync();
            (code, text) = await Werkzeug("status", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Gleich(0, code, "status über den Socket: " + text);
            Pruefe.Wahr(text.Contains("Funk-Server", StringComparison.Ordinal), "Statuszeile");
            (code, _) = await Werkzeug("netz-setzen", "--socket", t.Dienst.Verwaltungspfad, "--id", "4", "--name", "Brücke", "--offen", "HOEREN", "--standard");
            Pruefe.Gleich(0, code, "netz-setzen über die Kommandozeile");
            (code, text) = await Werkzeug("rolle-setzen", "--socket", t.Dienst.Verwaltungspfad, "--id", "2", "--name", "Navigator", "--selbst", "--feld", "0:4:h:s:std");
            Pruefe.Gleich(0, code, "rolle-setzen: " + text);
            (code, text) = await Werkzeug("netzliste", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Wahr(text.Contains("Brücke", StringComparison.Ordinal) && text.Contains("Navigator", StringComparison.Ordinal), "netzliste zeigt beides");
        }
        finally
        {
            Directory.Delete(verzeichnis, true);
        }
    }

    private static string Zeile(string text, string praefix) =>
        text.Split('\n', StringSplitOptions.TrimEntries).First(z => z.StartsWith(praefix, StringComparison.Ordinal))[praefix.Length..];

    /// <summary>Ruft funk-admin im selben Prozess auf und fängt die Ausgabe (auch für den Rundlauf in OberflaecheProfilTests).</summary>
    internal static async Task<(int Code, string Ausgabe)> Werkzeug(params string[] argumente)
    {
        var alt = Console.Out;
        var altFehler = Console.Error;
        var sb = new StringBuilder();
        using var schreiber = new StringWriter(sb);
        Console.SetOut(schreiber);
        Console.SetError(schreiber);
        try
        {
            var einstieg = typeof(Funk.Admin.Befehlszeile).Assembly.EntryPoint!;
            var ergebnis = einstieg.Invoke(null, [argumente]);
            var code = ergebnis is Task<int> task ? await task : (int)ergebnis!;
            return (code, sb.ToString());
        }
        finally
        {
            Console.SetOut(alt);
            Console.SetError(altFehler);
        }
    }
}
