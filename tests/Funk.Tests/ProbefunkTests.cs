// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.ClientKern.Echo;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Testserver;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Zusammenspiel im Probebetrieb: <c>funk-testserver --probe konfig\probefunk.json --echo</c> im Prozess, Clients über den
/// echten Loopback-Testzugang (Besitznachweis, automatische Freigabe), Sprache über UDP durch den Server. Geprüft wird, dass
/// eine Sprechphase im Netz Echo vollständig, in richtiger Reihenfolge und mit Pause zurückkommt, die 30-s-Grenze, eine
/// Phase zur Zeit, der Schleifenschutz mit zwei Echo-Funkern, und dass die automatische Freigabe nur am Testzugang hängt.
/// </summary>
internal static class ProbefunkTests
{
    private const ushort Echo = 2;

    private static IPAddress Lokal(int i) => new([127, 0, 9, (byte)(10 + i)]);

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    /// <summary>Die ausgelieferte Probe-Konfiguration aus <c>konfig\probefunk.json</c>.</summary>
    private static Probekonfiguration Konfiguration() => Probekonfiguration.Laden(Path.Combine(Wurzel(), "konfig", "probefunk.json"));

    private static Task<Probeserver> StartenAsync(ConcurrentQueue<string> log, EchoOptionen? echo = null, bool mitEcho = true) =>
        Probeserver.ProbeStartenAsync(Konfiguration(), Path.Combine(Testserver.Wurzel(), "probe"), new IPEndPoint(IPAddress.Loopback, 0), mitEcho,
            abgriff: log.Enqueue, echoOptionen: echo);

    private static string Letzte(ConcurrentQueue<string> log) => "\n  " + string.Join("\n  ", log.TakeLast(15));

    /// <summary>Opus-Nutzlast mit gültigem TOC (0xF8, 5.13), Lauf und laufender Nummer: jede Nutzlast ist eindeutig.</summary>
    private static byte[] Opus(byte lauf, int i)
    {
        var o = new byte[32];
        o[0] = 0xF8;
        o[1] = lauf;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(o.AsSpan(2), i);
        o.AsSpan(6).Fill(0x5A);
        return o;
    }

    /// <summary>Spricht eine Phase ins Netz Echo: Pre-roll als Stoß, dann Frames im 20-ms-Takt, dann ENDE. Liefert die Zeit nach dem ENDE.</summary>
    private static async Task<long> SprechenAsync(FunkClient c, byte lauf, int frames, int preroll = 0, bool va = true)
    {
        Pruefe.Wahr(c.Sprechen.Beginnen(Sendeziel.Netz(Echo), va), "Beginnen im Netz Echo");
        for (var i = 0; i < preroll; i++) Pruefe.Wahr(c.Sprechen.Senden(Opus(lauf, i), preroll: true), "Pre-roll gesendet");
        for (var i = preroll; i < frames; i++)
        {
            await Task.Delay(20);
            Pruefe.Wahr(c.Sprechen.Senden(Opus(lauf, i)), "Frame gesendet");
        }
        c.Sprechen.Beenden();
        return Monouhr.Ms();
    }

    [Test]
    public static async Task Probefunk_EchoKommtVollstaendigUndGeordnetZurueck()
    {
        var log = new ConcurrentQueue<string>();
        await using var s = await StartenAsync(log);
        var echo = s.Echo!;
        Pruefe.Gleich(Echo, echo.NetzId, "Echo-Funker hört im Netz Echo" + Letzte(log));
        await using var a = await Probesprecher.AktivAsync(s, 1);

        // Ohne Handarbeit im Netz Echo: STANDARD-Felder Echo (senden) und Kommando.
        Pruefe.Wahr(a.Client.Lage.FeldMitNetz(Echo) is { Hoeren: true, Senden: true }, "Feld Echo mit hören und senden");
        Pruefe.Wahr(a.Client.Lage.FeldMitNetz(3) is { Hoeren: true }, "Feld Kommando");
        Pruefe.Wahr(a.Client.Lage.Mitgliedschaften!.Sendeauswahl.Felder.SequenceEqual(new[] { a.Client.Lage.FeldMitNetz(Echo)!.Feld }), "Sendeauswahl des Servers ist das Feld Echo");

        var ende = await SprechenAsync(a.Client, lauf: 1, frames: 25, preroll: 3);
        var echoKonto = echo.Client.Lage.KontoId;
        Pruefe.Wahr(await a.Empfang.WartenAsync(p => p.Any(x => x.Absender == echoKonto && x.Ende), TimeSpan.FromSeconds(6)),
            "Echo mit ENDE zurück" + Letzte(log));
        var zurueck = a.Empfang.Von(echoKonto);
        var audio = zurueck.Where(p => !p.Ende).ToList();
        Pruefe.Gleich(25, audio.Count, "Paketzahl");
        for (var i = 0; i < 25; i++) Pruefe.BytesGleich(Opus(1, i), audio[i].Opus, "Reihenfolge und Inhalt, Frame " + i);
        Pruefe.Wahr(audio.Zip(audio.Skip(1)).All(x => x.Second.Frame == x.First.Frame + 1), "Frame-Nr lückenlos steigend");
        Pruefe.Wahr(audio.All(p => p.Phase == audio[0].Phase), "eine Sprechphase");
        Pruefe.Wahr((audio[0].Kennzeichen & Kennzeichen.Beginn) != 0 && audio.Skip(1).All(p => (p.Kennzeichen & Kennzeichen.Beginn) == 0), "BEGINN nur am ersten");
        Pruefe.Wahr(audio.Take(3).All(p => (p.Kennzeichen & Kennzeichen.Preroll) != 0) && audio.Skip(3).All(p => (p.Kennzeichen & Kennzeichen.Preroll) == 0), "Pre-roll wie beim Sprecher");
        Pruefe.Wahr(audio.All(p => (p.Kennzeichen & Kennzeichen.Va) != 0), "VA wie beim Sprecher");
        Pruefe.Wahr(audio.All(p => p.Art == Spruchart.Netz && p.Netze.SequenceEqual(new[] { Echo })), "NETZ im Netz Echo");
        Pruefe.Wahr(audio[0].NeueZeitlinie && audio.Skip(1).All(p => !p.NeueZeitlinie), "eine Zeitlinie beim Empfänger");
        Pruefe.Gleich(1, zurueck.Count(p => p.Ende), "ein ENDE gemeldet");
        var pause = audio[0].AnkunftMs - ende;
        Pruefe.Wahr(pause is >= 500 and < 2500, "Pause nach dem ENDE: " + pause + " ms");
        Pruefe.Wahr(a.Empfang.Pakete.All(p => p.Absender == echoKonto), "nur das Echo, nie der eigene Spruch");
        Pruefe.Gleich(1, echo.Aufnahmen, "eine Aufnahme");
        Pruefe.Wahr(await Warten(() => echo.Wiedergaben == 1 && echo.Zustand == EchoZustand.Bereit, 2000), "eine Wiedergabe, wieder bereit");
    }

    [Test]
    public static async Task Probefunk_GrenzeUndEinePhaseZurZeit()
    {
        var log = new ConcurrentQueue<string>();
        // 200 ms statt 30 s, damit die Grenze im Test schnell erreicht ist; die 30 s selbst prüft EchoTests.
        await using var s = await StartenAsync(log, new EchoOptionen { MaxPhase = TimeSpan.FromMilliseconds(200) });
        var echoKonto = s.Echo!.Client.Lage.KontoId;
        await using var a = await Probesprecher.AktivAsync(s, 2);
        await using var b = await Probesprecher.AktivAsync(s, 3);

        await SprechenAsync(a.Client, lauf: 2, frames: 15);
        // B spricht, während das Echo von A in der Pause ist: wird nicht aufgenommen.
        await SprechenAsync(b.Client, lauf: 3, frames: 5);
        Pruefe.Wahr(await a.Empfang.WartenAsync(p => p.Any(x => x.Absender == echoKonto && x.Ende), TimeSpan.FromSeconds(6)), "Echo von A" + Letzte(log));
        var echoA = a.Empfang.Von(echoKonto).Where(p => !p.Ende).ToList();
        Pruefe.Gleich(10, echoA.Count, "gekürzt auf die Höchstdauer (10 Frames)");
        for (var i = 0; i < 10; i++) Pruefe.BytesGleich(Opus(2, i), echoA[i].Opus, "Anfang der Phase, Frame " + i);
        Pruefe.Wahr(await Warten(() => s.Echo.Zustand == EchoZustand.Bereit, 2000), "Echo wieder bereit");
        await Task.Delay(700);
        Pruefe.Wahr(b.Empfang.Von(echoKonto).Where(p => !p.Ende).All(p => p.Opus[1] == 2), "B hört nur das Echo von A, nicht das eigene");
        Pruefe.Gleich(1, s.Echo.Aufnahmen, "B wurde nicht aufgenommen");
        Pruefe.Gleich(1, s.Echo.Gekuerzt, "eine gekürzte Phase");

        // Jetzt ist B dran.
        b.Empfang.Leeren();
        await SprechenAsync(b.Client, lauf: 4, frames: 5);
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Any(x => x.Absender == echoKonto && x.Ende), TimeSpan.FromSeconds(6)), "Echo von B" + Letzte(log));
        var echoB = b.Empfang.Von(echoKonto).Where(p => !p.Ende).ToList();
        Pruefe.Gleich(5, echoB.Count, "B bekommt seine 5 Frames");
        Pruefe.Wahr(echoB.All(p => p.Opus[1] == 4), "Inhalt von B");
        Pruefe.Wahr((echoB[0].Kennzeichen & Kennzeichen.Va) != 0 && echoB.All(p => (p.Kennzeichen & Kennzeichen.Preroll) == 0), "VA ohne Pre-roll");
    }

    [Test]
    public static async Task Probefunk_ZweiEchoFunkerSchaukelnSichNichtAuf()
    {
        var log = new ConcurrentQueue<string>();
        // Der Echo-Funker im Server wartet 1,5 s; so hört der zweite nur dessen Echo, nicht den Sprecher.
        await using var s = await StartenAsync(log, new EchoOptionen { Pause = TimeSpan.FromMilliseconds(1500) });
        var e1 = s.Echo!;
        await using var a = await Probesprecher.AktivAsync(s, 4);
        var id2 = Geraeteidentitaet.Fluechtig();
        await using var e2 = new EchoFunker(new TestzugangAufbau(s.Testzugang, id2, Lokal(5)), id2, new EchoOptionen(), t => log.Enqueue("E2: " + t));
        e2.Starten();
        Pruefe.Wahr(await e2.Client.AktivAsync(TimeSpan.FromSeconds(10)) && await Warten(() => e2.NetzId == Echo, 3000), "zweiter Echo-Funker bereit" + Letzte(log));
        Pruefe.Wahr((await e2.Client.HoerenSetzenAsync(Echo, false)).Ok, "E2 hört zunächst nicht");

        await SprechenAsync(a.Client, lauf: 5, frames: 10);
        Pruefe.Wahr((await e2.Client.HoerenSetzenAsync(Echo, true)).Ok, "E2 hört ab jetzt");
        Pruefe.Wahr(await Warten(() => e1.Schleifen == 1, 8000), "E1 erkennt sein eigenes Echo über E2" + Letzte(log));
        await Task.Delay(1500);

        var e1Konto = e1.Client.Lage.KontoId;
        var e2Konto = e2.Client.Lage.KontoId;
        var phasen = a.Empfang.Pakete.Where(p => !p.Ende).Select(p => (p.Absender, p.Phase)).Distinct().ToList();
        Pruefe.Gleich(2, phasen.Count, "genau zwei Echos beim Sprecher: von E1 und das Echo davon von E2");
        Pruefe.Wahr(phasen.Any(p => p.Absender == e1Konto) && phasen.Any(p => p.Absender == e2Konto), "je eines von E1 und E2");
        Pruefe.Wahr(a.Empfang.Pakete.Where(p => !p.Ende).All(p => p.Opus[1] == 5), "beide mit dem Inhalt des Sprechers");
        Pruefe.Gleich(1, e1.Aufnahmen, "E1 nimmt nur den Sprecher auf");
        Pruefe.Gleich(1, e1.Wiedergaben, "E1 gibt einmal wieder");
        Pruefe.Gleich(1, e2.Aufnahmen, "E2 nimmt das Echo von E1 auf");
        Pruefe.Gleich(1, e2.Wiedergaben, "E2 gibt einmal wieder");
        Pruefe.Gleich(0, e2.Schleifen, "E2 kennt den Inhalt nicht als eigenen");
    }

    [Test]
    public static async Task Probefunk_KonfigurationRollenUndSchiffe()
    {
        var log = new ConcurrentQueue<string>();
        await using var s = await StartenAsync(log, mitEcho: false);
        Pruefe.Wahr(s.Echo is null, "ohne --echo kein Echo-Funker");
        await using var a = await Probesprecher.AktivAsync(s, 6);
        var liste = a.Client.Lage.NetzListe!;
        Pruefe.Gleich(11, liste.Netze.Count, "Netze aus probefunk.json");
        foreach (var name in new[] { "Global", "Echo", "Kommando", "Durchsagen", "Reserve", "Schiff 1 Crew", "Schiff 3 Brücke" })
            Pruefe.Wahr(liste.Netze.Any(n => n.Name == name), "Netz " + name);
        Pruefe.Wahr(liste.Netze.Single(n => n.Id == 12) is { SchiffId: 1, Schiffsnetz: "BRUECKE" }, "Schiffsnetz mit Bindung");
        Pruefe.Gleich(4, liste.Rollen.Count, "Rollen");
        Pruefe.Gleich(3, liste.Schiffe.Count, "Schiffe");
        Pruefe.Wahr(liste.Einstellungen is { Selbstwahl: true, DurchsageNurTaste: true }, "Einstellungen");
        Pruefe.Wahr(a.Client.Lage.HatRecht(Recht.NetzeVerwalten), "Probekonto mit der Stufe aus der Konfiguration (ADMIN)");

        // Rolle und Schiff in einem Schritt (A15): Pilot auf Schiff 1, Echo bleibt.
        var antwort = await a.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = 1, SchiffId = 1 });
        Pruefe.Wahr(antwort.Ok, "EinsteigenRolleSchiff: " + antwort.Code);
        Pruefe.Wahr(await Warten(() => a.Client.Lage.FeldMitNetz(12) is { Senden: true }, 3000), "Brücke von Schiff 1 mit senden");
        Pruefe.Wahr(a.Client.Lage.FeldMitNetz(11) is { Hoeren: true } && a.Client.Lage.FeldMitNetz(Echo) is { Senden: true }, "Crew hören, Echo bleibt");
    }

    [Test]
    public static async Task Probefunk_FreigabeNurUeberDenTestzugang()
    {
        var log = new ConcurrentQueue<string>();
        await using var s = await StartenAsync(log, mitEcho: false);
        // Derselbe Dienst, aber am Testzugang vorbei (der Weg nach einem TLS-Handshake): unbekanntes Gerät, Registrierung.
        var id = Geraeteidentitaet.Fluechtig();
        var code = new TaskCompletionSource<Registrierungscode>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = new DelegatAufbau(async (wiederverbindung, abbruch) =>
            {
                using var lauscher = new TcpListener(IPAddress.Loopback, 0);
                lauscher.Start();
                var c = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                c.Bind(new IPEndPoint(Lokal(7), 0));
                var annahme = lauscher.AcceptSocketAsync(abbruch);
                await c.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)lauscher.LocalEndpoint).Port, abbruch);
                var serverSeite = await annahme;
                _ = Task.Run(() => s.Dienst.NachHandshakeAsync(new NetworkStream(serverSeite, ownsSocket: true), serverSeite,
                    Adresse.Aus((IPEndPoint)serverSeite.RemoteEndPoint!), id.Fingerabdruck), CancellationToken.None);
                return new Steuerverbindung(new NetworkStream(c, ownsSocket: true), IPAddress.Loopback, Lokal(7), beschreibung: "am Testzugang vorbei");
            }),
            Identitaet = id,
            ClientVersion = "1.0.0",
            Geraetename = "Fremd",
        });
        client.CodeErhalten += (_, c) => code.TrySetResult(c);
        try
        {
            client.Starten();
            await code.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Pruefe.Gleich(ClientZustand.Registrierung, client.Zustand, "ohne Testzugang: Registrierung wie beim echten Server");
            await Task.Delay(1000);
            Pruefe.Wahr(client.Zustand is not (ClientZustand.AktivUdp or ClientZustand.AktivTunnel), "keine automatische Freigabe");
            Pruefe.Wahr(s.Dienst.Zentrale.Konten.Stand.GeraetZuFingerabdruck(id.Fingerabdruck) is null, "kein Gerät angelegt");
        }
        finally
        {
            await client.DisposeAsync();
            id.Dispose();
        }

        // Und der Testzugang selbst nimmt nur Loopback an.
        try
        {
            await Probeserver.ProbeStartenAsync(Konfiguration(), Path.Combine(Testserver.Wurzel(), "probe"), new IPEndPoint(IPAddress.Parse("192.168.1.2"), 0), false);
            throw new PruefungException("Testzugang außerhalb von Loopback angenommen");
        }
        catch (ArgumentException)
        {
        }
    }

    [Test]
    public static async Task Probefunk_KonfigwegMitEchoUndSpaeteremNetz()
    {
        // funk-testserver --konfig <server.json> --testzugang <port> --echo --freigabe-automatisch FUNKER: Schlüssel,
        // Geheimnis und Profil als Dateien wie im Lasttest. Das Netz Echo gibt es erst nach dem Start; der Funker wartet
        // und trägt sich dann selbst ein.
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "konfig-echo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(verzeichnis);
        var mitschrift = new StringWriter();
        var konsole = TextWriter.Synchronized(mitschrift);
        string Mitschrift()
        {
            lock (konsole) return mitschrift.ToString();
        }
        try
        {
            KonfigDateienSchreiben(verzeichnis);
            var k = new ServerKonfiguration
            {
                Binden = ["127.0.0.1"],
                TcpPort = 0,
                UdpPort = 0,
                Zustandsverzeichnis = "zustand",
                Identitaet = new IdentitaetsDateien { Zertifikat = "server.crt", Schluessel = "server.key" },
                Geheimnis = "geheimnis.bin",
                Profil = "profil.json",
                VerwaltungsSocket = "v.sock",
                Test = new TestKonfiguration { ZufallsPorts = true },
                Basisverzeichnis = verzeichnis,
            };
            await using var s = await Probeserver.KonfigStartenAsync(k, new IPEndPoint(IPAddress.Loopback, 0), echo: true,
                freigabeStufe: I.Stufe.Funker, konsole: konsole);
            var echo = s.Echo!;
            Pruefe.Gleich((ushort)0, echo.NetzId, "ohne Netz Echo wartet der Funker");
            Pruefe.Wahr(Mitschrift().Contains("Echo-Funker wartet", StringComparison.Ordinal), "Hinweis auf das fehlende Netz");

            var netz = new JsonObject
            {
                ["id"] = Echo, ["name"] = "Echo", ["typ"] = "NORMAL", ["reihenfolge"] = 1, ["nurTaste"] = false,
                ["ausloeserStandard"] = "VA", ["offen"] = "HOEREN_SENDEN", ["standard"] = false,
            };
            var angelegt = await s.Dienst.Zentrale.VerwaltungAsync("netz-setzen", new JsonObject { ["objekt"] = netz });
            Pruefe.Wahr(angelegt.Ok, "netz-setzen: " + angelegt.Text);
            Pruefe.Wahr(await Warten(() => echo.NetzId == Echo && echo.Client.Lage.FeldMitNetz(Echo) is { Hoeren: true, Senden: true }, 5000),
                "Funker trägt sich ins spätere Netz Echo ein\n" + Mitschrift());

            await using var a = await Probesprecher.AktivAsync(s, 8);
            Pruefe.Falsch(a.Client.Lage.HatRecht(Recht.NetzeVerwalten), "automatische Freigabe mit der Stufe FUNKER");
            Pruefe.Wahr((await a.Client.FeldSetzenAsync(0, Echo)).Ok, "Sprecher setzt sich ins offene Netz Echo");
            Pruefe.Wahr(await Warten(() => a.Client.Lage.FeldMitNetz(Echo) is { Senden: true }, 3000), "Feld Echo mit senden");

            await SprechenAsync(a.Client, lauf: 8, frames: 12, preroll: 2);
            var echoKonto = echo.Client.Lage.KontoId;
            Pruefe.Wahr(await a.Empfang.WartenAsync(p => p.Any(x => x.Absender == echoKonto && x.Ende), TimeSpan.FromSeconds(6)),
                "Echo mit ENDE zurück\n" + Mitschrift());
            var audio = a.Empfang.Von(echoKonto).Where(p => !p.Ende).ToList();
            Pruefe.Gleich(12, audio.Count, "Paketzahl");
            for (var i = 0; i < 12; i++) Pruefe.BytesGleich(Opus(8, i), audio[i].Opus, "Reihenfolge und Inhalt, Frame " + i);
        }
        finally
        {
            for (var versuch = 0; versuch < 10 && Directory.Exists(verzeichnis); versuch++)
            {
                try
                {
                    Directory.Delete(verzeichnis, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(200);
                }
            }
        }
    }

    /// <summary>Serverschlüssel, Betriebsgeheimnis und signiertes Profil eines Laufs als Dateien, wie sie der Lasttest ablegt.</summary>
    private static void KonfigDateienSchreiben(string verzeichnis)
    {
        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var zert = new CertificateRequest("CN=Funk-Testserver", schluessel, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(Path.Combine(verzeichnis, "server.crt"), zert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(verzeichnis, "server.key"), schluessel.ExportPkcs8PrivateKeyPem());
        Geheimnis.Erzeugen(Path.Combine(verzeichnis, "geheimnis.bin"));
        TotpSchluessel.Erzeugen(Path.Combine(verzeichnis, "totp-schluessel.bin"));
        using var profilSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = DateTimeOffset.UtcNow;
        var profil = new I.Serverprofil("Konfigweg", 1, jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(1).ToUnixTimeMilliseconds(),
            [new I.ProfilHost("localhost", true, false)], 443, [I.Fingerabdruck.AusZertifikat(zert), I.Fingerabdruck.AusSchluessel(reserve)], []);
        File.WriteAllBytes(Path.Combine(verzeichnis, "profil.json"), I.SigniertesProfil.Signieren(profil.AlsJson(), profilSchluessel).AlsJson());
    }

    [Test]
    public static void Probekonfiguration_PrueftIhreRegeln()
    {
        var k = Konfiguration();
        Pruefe.Gleich(0, k.Pruefen().Count, "ausgelieferte probefunk.json ist gültig: " + string.Join(" ", k.Pruefen()));
        Pruefe.Gleich("Echo", k.Echo.Netz, "Echo-Netz");
        Pruefe.Gleich(TimeSpan.FromSeconds(30), k.Echo.Optionen().MaxPhase, "30 s");

        var ordner = Path.Combine(Testserver.Wurzel(), "probe-konfig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ordner);
        try
        {
            string Datei(string inhalt)
            {
                var p = Path.Combine(ordner, Guid.NewGuid().ToString("N")[..8] + ".json");
                File.WriteAllText(p, inhalt);
                return p;
            }
            Pruefe.Wirft<InvalidDataException>(() => Probekonfiguration.Laden(Datei("""{ "nme": "Tippfehler" }""")), "unbekanntes Feld");
            var ohneEcho = Probekonfiguration.Laden(Datei("""{ "netze": [ { "id": 3, "name": "Kommando" } ] }"""));
            Pruefe.Wahr(ohneEcho.Pruefen().Any(f => f.Contains("echo.netz", StringComparison.Ordinal)), "Echo-Netz fehlt");
            var stufe = Probekonfiguration.Laden(Datei("""{ "freigabe": { "stufe": "KOENIG" }, "netze": [ { "id": 2, "name": "Echo" } ] }"""));
            Pruefe.Wahr(stufe.Pruefen().Any(f => f.Contains("freigabe.stufe", StringComparison.Ordinal)), "Stufe ungültig");
            var lang = Probekonfiguration.Laden(Datei("""{ "echo": { "maxSekunden": 31 }, "netze": [ { "id": 2, "name": "Echo" } ] }"""));
            Pruefe.Wahr(lang.Pruefen().Any(f => f.StartsWith("echo:", StringComparison.Ordinal)), "mehr als 30 s");
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }
    }

    [Test]
    public static void Probefunk_SocketpfadBleibtUnter108Zeichen()
    {
        // UNIX-Sockets erlauben 108 Zeichen; ein Paket tief im Dokumente-Ordner lag gemessen bei 116.
        var kurz = @"C:\Claude\tmp\tr-probefunk\probe-zustand\20260929-162842-064";
        Pruefe.Gleich(Path.Combine(kurz, "v.sock"), Probeserver.SocketPfad(kurz), "kurzer Pfad: im Zustand");
        var lang = @"C:\Users\Beispielname\OneDrive\Dokumente\MeinClan\20260929_1627_JG-TacRelay\probe-zustand\20260929-162842-064";
        var ausweich = Probeserver.SocketPfad(lang);
        Pruefe.Wahr(!ausweich.StartsWith(lang, StringComparison.Ordinal) && ausweich.Length <= 108 && ausweich.EndsWith(".sock", StringComparison.Ordinal),
            "langer Pfad: Ausweichen (" + ausweich.Length + " Zeichen)");
        // Wo es C:\Claude\tmp gibt (Probeläufe durch Claude), weicht der Socket dorthin aus, nie ins Temp-Verzeichnis des
        // Benutzers; nur auf einem PC ohne C:\Claude bleibt das Temp-Verzeichnis.
        if (Directory.Exists(Probeserver.Zonenordner))
        {
            Pruefe.Gleich(Probeserver.Zonenordner, Probeserver.Ausweichordner, "Ausweichordner in der Zone");
            Pruefe.Wahr(ausweich.StartsWith(Probeserver.Zonenordner + @"\tr-probe-", StringComparison.OrdinalIgnoreCase), "langer Pfad: in C:\\Claude\\tmp, war " + ausweich);
        }
        var anderswo = Probeserver.SocketPfad(lang, @"D:\x");
        Pruefe.Wahr(anderswo.StartsWith(@"D:\x\tr-probe-", StringComparison.Ordinal), "vorgegebener Ausweichordner: " + anderswo);

        // Die Grenze gilt für UTF-8-Bytes samt Nullbyte, nicht für Zeichen: 100 Zeichen mit Umlauten sind 108 Byte, und
        // .NET lehnt den Pfad ab. Mit der Zählung nach Zeichen wäre der Start daran gescheitert.
        var umlaute = @"C:\Übungsräumen\Größenprüfung\Überführungsflüge\JG-TacRelay\probe-zustand\20260929-162842-064";
        var imVerzeichnis = Path.Combine(umlaute, "v.sock");
        Pruefe.Gleich(100, imVerzeichnis.Length, "100 Zeichen");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => _ = new UnixDomainSocketEndPoint(imVerzeichnis), ".NET lehnt 108 Byte ab");
        var mitUmlauten = Probeserver.SocketPfad(umlaute);
        Pruefe.Wahr(!mitUmlauten.StartsWith(umlaute, StringComparison.Ordinal), "Umlaute: Ausweichen");
        foreach (var pfad in new[] { Probeserver.SocketPfad(kurz), ausweich, mitUmlauten })
            Pruefe.Gleich(AddressFamily.Unix, new UnixDomainSocketEndPoint(pfad).AddressFamily, ".NET nimmt den Socketpfad an: " + pfad);
    }

    [Test]
    public static void Probefunk_BegleitprogrammArgumente()
    {
        Pruefe.Wahr(Begleitprogramm.Argumente("--testzugang {testzugang}").SequenceEqual(["--testzugang", "{testzugang}"]), "Standard");
        Pruefe.Wahr(Begleitprogramm.Argumente("echo-probe  --netz \"Mein Echo\" --frames 50 ").SequenceEqual(["echo-probe", "--netz", "Mein Echo", "--frames", "50"]),
            "Anführungszeichen und doppelte Leerzeichen");
        Pruefe.Wahr(Begleitprogramm.Argumente("a \"\" b").SequenceEqual(["a", "", "b"]), "leeres Argument");
        Pruefe.Wirft<ArgumentException>(() => Begleitprogramm.Argumente("\"offen"), "offenes Anführungszeichen");
    }

    [Test]
    public static void Probefunk_TacRelayAlsBegleitprogrammNurFreigegeben()
    {
        // Der Client zeigt Fenster und öffnet Geräte: Ein Werkzeuglauf wie in BETRIEB.md startet ihn nicht still.
        Pruefe.Wahr(Begleitprogramm.Sperre(@"C:\x\client\TacRelay.exe", freigegeben: false) is not null, "JG TacRelay ohne Freigabe gesperrt");
        Pruefe.Wahr(Begleitprogramm.Sperre("tacrelay", freigegeben: false) is not null, "auch ohne Endung und klein geschrieben");
        Pruefe.Gleich(null, Begleitprogramm.Sperre(@"C:\x\client\TacRelay.exe", freigegeben: true), "mit --client-freigegeben");
        Pruefe.Gleich(null, Begleitprogramm.Sperre(@"C:\x\server\funk-testclient.exe", freigegeben: false), "funk-testclient braucht keine Freigabe");

        // Sarevocs Probefunk bleibt, wie er ist; die Prüfung ohne Fenster startet JG TacRelay nicht.
        var paket = Path.Combine(Wurzel(), "tools", "paket");
        var starten = File.ReadAllText(Path.Combine(paket, "Probefunk starten.cmd"));
        Pruefe.Wahr(starten.Contains(@"client\TacRelay.exe"" --client-freigegeben", StringComparison.Ordinal), "Probefunk starten.cmd gibt den Client frei");
        var pruefen = File.ReadAllText(Path.Combine(paket, "Probefunk pruefen.cmd"));
        Pruefe.Falsch(pruefen.Contains("TacRelay.exe", StringComparison.OrdinalIgnoreCase), "Probefunk pruefen.cmd startet keinen Client mit Fenster");
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = Environment.TickCount64 + ms;
        while (!bedingung())
        {
            if (Environment.TickCount64 > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }
}

/// <summary>Ein Sprachpaket mit Empfangszeit (monotone ms).</summary>
internal sealed record Mitgeschnitten(uint Absender, ushort Phase, uint Frame, Kennzeichen Kennzeichen, Spruchart Art, ushort[] Netze, byte[] Opus,
    bool NeueZeitlinie, bool Ende, long AnkunftMs);

/// <summary>Sammelt empfangene Sprache samt Empfangszeit.</summary>
internal sealed class Mitschnitt : ISprachEmpfaenger
{
    private readonly ConcurrentQueue<Mitgeschnitten> _pakete = new();

    public IReadOnlyCollection<Mitgeschnitten> Pakete => _pakete;

    public void Empfangen(in SprachEmpfang e)
    {
        var netze = new ushort[e.Paket.NetzAnzahl];
        for (var i = 0; i < netze.Length; i++) netze[i] = e.Paket.Netz(i);
        _pakete.Enqueue(new Mitgeschnitten(e.Absender, e.Sprechphase, e.Paket.FrameNr, e.Paket.Kennzeichen, e.Paket.Art, netze, e.Paket.Opus.ToArray(),
            e.NeueZeitlinie, e.Ende, Monouhr.MsAus(e.EmpfangenTakte)));
    }

    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
    }

    public void Leeren() => _pakete.Clear();

    public List<Mitgeschnitten> Von(uint absender) => [.. _pakete.Where(p => p.Absender == absender)];

    public async Task<bool> WartenAsync(Func<IReadOnlyCollection<Mitgeschnitten>, bool> bedingung, TimeSpan frist)
    {
        var bis = DateTime.UtcNow + frist;
        while (!bedingung(_pakete))
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }
}

/// <summary>Ein Client über den Loopback-Testzugang des Probeservers, mit flüchtigem Schlüssel wie der Client im Testzugang.</summary>
internal sealed class Probesprecher : IAsyncDisposable
{
    private Probesprecher(FunkClient client, Geraeteidentitaet identitaet, Mitschnitt empfang)
    {
        Client = client;
        Identitaet = identitaet;
        Empfang = empfang;
    }

    public FunkClient Client { get; }
    public Geraeteidentitaet Identitaet { get; }
    public Mitschnitt Empfang { get; }

    /// <summary>Verbindet über den Testzugang und wartet auf AKTIV/UDP; keine Registrierung, kein Admin-Werkzeug.</summary>
    public static async Task<Probesprecher> AktivAsync(Probeserver s, int i)
    {
        var identitaet = Geraeteidentitaet.Fluechtig();
        var empfang = new Mitschnitt();
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = new TestzugangAufbau(s.Testzugang, identitaet, new IPAddress([127, 0, 9, (byte)(10 + i)])),
            Identitaet = identitaet,
            ClientName = "Probetest",
            ClientVersion = "1.0.0",
            Geraetename = "Probe " + i,
            Empfaenger = empfang,
            Zufall = () => 0.0,
        });
        var p = new Probesprecher(client, identitaet, empfang);
        client.Starten();
        if (!await client.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(10)))
        {
            var zustand = client.Zustand;
            await p.DisposeAsync();
            throw new PruefungException("Probesprecher " + i + " nicht AKTIV/UDP, sondern " + zustand);
        }
        return p;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Identitaet.Dispose();
    }
}
