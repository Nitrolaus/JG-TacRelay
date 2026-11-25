// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Verbindungsaufbau der Client-Bibliothek: RFC 8305 (ohne TLS, nur Loopback), echtes TLS mit Pin und ALPN (unter
/// Windows nur mit <c>FUNK_TLS_IM_PROFIL=1</c>, weil Schannel Schlüssel im Benutzerprofil ablegt; unter Linux immer),
/// dazu die Client-Regel "kein automatischer Neuversuch nach Bye{VERDRAENGT}" (4.6).
/// </summary>
internal static class ClientTlsTests
{
    private static Serverprofil Profil(params Fingerabdruck[] pins) =>
        new("Test", 1, 0, long.MaxValue / 2, [new ProfilHost("localhost", true, false)], 443, pins, []);

    [Test]
    public static async Task KernTls_Rfc8305_ErsteErreichbareAdresseGewinnt()
    {
        using var lauscher = new TcpListener(IPAddress.Loopback, 0);
        lauscher.Start();
        var port = (ushort)((IPEndPoint)lauscher.LocalEndpoint).Port;
        using var id = Geraeteidentitaet.Fluechtig();
        using var p = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var r = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var aufbau = new TlsAufbau(Profil(Fingerabdruck.AusSchluessel(p), Fingerabdruck.AusSchluessel(r)), id);

        // 127.0.0.2 lauscht nicht (Verbindung abgelehnt, unter Windows mit SYN-Wiederholungen etwa 2 s); nach 250 ms
        // startet der zweite Versuch und gewinnt (RFC 8305).
        var uhr = Stopwatch.StartNew();
        var (socket, ziel) = await aufbau.TcpAsync([(IPAddress.Parse("127.0.0.2"), "localhost"), (IPAddress.Loopback, "localhost")], port, CancellationToken.None);
        using (socket)
        {
            Pruefe.Wahr(ziel.Ip.Equals(IPAddress.Loopback), "die erreichbare Adresse gewinnt");
            Pruefe.Wahr(uhr.ElapsedMilliseconds < 2000, "ohne auf den ersten Versuch zu warten: " + uhr.ElapsedMilliseconds + " ms");
            Pruefe.Wahr(socket.NoDelay, "NoDelay (4.1)");
        }

        var fehler = await Wirft(() => aufbau.TcpAsync([(IPAddress.Parse("127.0.0.3"), "localhost")], port, CancellationToken.None));
        Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.Netz }, "keine Adresse erreichbar: Netz, war " + fehler?.GetType().Name);
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

    [Test]
    public static async Task KernTls_EchterHandshakeMitPinUndPinFehler()
    {
        if (!Testserver.TlsErlaubt)
        {
            Console.WriteLine("HINWEIS KernTls_EchterHandshakeMitPinUndPinFehler übersprungen: unter Windows nur mit FUNK_TLS_IM_PROFIL=1 (Schannel legt Schlüssel im Benutzerprofil ab).");
            return;
        }
        await using var t = await Testserver.StartenAsync(tls: true);
        Pruefe.Wahr(Funk.Protokoll.Identitaet.SigniertesProfil.TryLesen(File.ReadAllBytes(Path.Combine(t.Verzeichnis, "profil.json")), out var angebot), "Profil lesbar");
        Pruefe.Wahr(Serverprofil.TryLesen(angebot!.Daten, out var gelesen), "Profildaten gültig");
        var profil = gelesen!;
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = new JsonObject { ["id"] = 2, ["name"] = "Kommando", ["typ"] = "NORMAL", ["reihenfolge"] = 0, ["nurTaste"] = false, ["ausloeserStandard"] = "TASTE", ["offen"] = "HOEREN_SENDEN", ["standard"] = true },
        });
        var port = (ushort)t.Dienst.TcpEndpunkte[0].Port;

        async Task<(FunkClient Client, Geraeteidentitaet Id, Sammler Empfang)> AnmeldenAsync(string discord, string benutzer, byte adresse)
        {
            var id = Geraeteidentitaet.Fluechtig();
            var empfang = new Sammler();
            var c = new FunkClient(new FunkClientOptionen
            {
                Aufbau = new TlsAufbau(profil, id, new TlsAufbauOptionen { Host = "127.0.0.1", Port = port, LokaleAdresse = new IPAddress([127, 0, 9, adresse]) }),
                Identitaet = id,
                ClientVersion = "1.0.0",
                Rueckfrage = (_, _) => Task.FromResult(true),
                Empfaenger = empfang,
            });
            var code = new TaskCompletionSource<Registrierungscode>(TaskCreationOptions.RunContinuationsAsynchronously);
            c.CodeErhalten += (_, x) => code.TrySetResult(x);
            c.Starten();
            var k = await code.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await t.AdminOk("verknuepfen", new JsonObject { ["code"] = k.Code, ["discord"] = discord, ["benutzername"] = benutzer });
            Pruefe.Wahr(await c.WartenAufAsync(z => z == ClientZustand.Warteraum, TimeSpan.FromSeconds(5)), "WARTERAUM über TLS");
            await t.AdminOk("freigeben", new JsonObject { ["geraet"] = id.Fingerabdruck.Anzeige });
            Pruefe.Wahr(await c.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(10)), "AKTIV/UDP über TLS, war " + c.Zustand);
            return (c, id, empfang);
        }

        var (a, ida, _) = await AnmeldenAsync("400000000000000001", "tlsa", 2);
        var (b, idb, eb) = await AnmeldenAsync("400000000000000002", "tlsb", 3);
        try
        {
            Pruefe.Wahr(a.Sprechen.Beginnen(Sendeziel.Netz(2)), "A spricht über TLS-Sitzung");
            for (var i = 0; i < 5; i++) a.Sprechen.Senden(KernFunker.Opus);
            a.Sprechen.Beenden();
            Pruefe.Wahr(await eb.WartenAsync(x => x.Count(y => !y.Ende) >= 5, TimeSpan.FromSeconds(3)), "B hört A");
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
            ida.Dispose();
            idb.Dispose();
        }

        // Falscher Pin bei der Erstverbindung: sofort "Server nicht vertrauenswürdig", ohne Ausnahmeknopf (4.1).
        using var falsch1 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var falsch2 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var idc = Geraeteidentitaet.Fluechtig();
        var aufbau = new TlsAufbau(Profil(Fingerabdruck.AusSchluessel(falsch1), Fingerabdruck.AusSchluessel(falsch2)), idc,
            new TlsAufbauOptionen { Host = "127.0.0.1", Port = port, LokaleAdresse = new IPAddress([127, 0, 9, 4]) });
        var fehler = await Wirft(() => aufbau.AufbauenAsync(wiederverbindung: false, CancellationToken.None));
        Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.PinAlarm }, "Pin-Fehler bei der Erstverbindung: Alarm, war " + fehler?.Message);
        fehler = await Wirft(() => aufbau.AufbauenAsync(wiederverbindung: true, CancellationToken.None));
        Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.Pin }, "beim Wiederverbinden zuerst falscher Host, kein Alarm (7.1 Schritt 4)");
        Pruefe.Falsch(aufbau.PinAlarm, "Alarm erst nach 2 min");
    }

    /// <summary>4.6: Nach Bye{VERDRAENGT} kein automatischer Neuversuch; "zurückholen" verbindet auf Wunsch des Nutzers neu.</summary>
    [Test]
    public static async Task Kern_Verdraengung_KeinAutomatischerNeuversuch()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        const string discord = "500000000000000001";
        await using var alt = await KernFunker.AktivAsync(halter, discord, "vera", "FUNKER", new IPAddress([127, 0, 8, 2]));
        await using var neu = await KernFunker.AktivAsync(halter, discord, "vera", "FUNKER", new IPAddress([127, 0, 8, 3]));

        Pruefe.Wahr(await alt.Client.WartenAufAsync(z => z == ClientZustand.Getrennt, TimeSpan.FromSeconds(3)), "das alte Gerät ist getrennt");
        // Der Zustand wechselt schon im Automaten auf Getrennt; SitzungBeendet folgt erst nach dem Abbau der Sitzung.
        Pruefe.Wahr(await EndeAbwarten(alt, ByeGrund.Verdraengt), "Bye{VERDRAENGT}");
        var ende = alt.Enden.FirstOrDefault(e => e.Bye == ByeGrund.Verdraengt);
        Pruefe.Wahr(ende is not null, "Bye{VERDRAENGT}");
        Pruefe.Gleich(neu.Identitaet.Fingerabdruck.Anzeige, ende!.Durch?.Anzeige, "verdrängt durch das neue Gerät (Anzeigeform)");
        Pruefe.Wahr(alt.Client.NeuversuchGesperrt, "Neuversuch gesperrt");
        await Task.Delay(2500);
        Pruefe.Gleich(ClientZustand.Getrennt, alt.Client.Zustand, "kein automatischer Neuversuch");

        // "zurückholen": Der Nutzer verbindet neu; jetzt wird das andere Gerät verdrängt.
        alt.Client.NeuVerbinden();
        Pruefe.Wahr(await alt.Client.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(5)), "zurückgeholt");
        Pruefe.Wahr(await neu.Client.WartenAufAsync(z => z == ClientZustand.Getrennt, TimeSpan.FromSeconds(3)), "jetzt ist das neue Gerät verdrängt");
        Pruefe.Wahr(await EndeAbwarten(neu, ByeGrund.Verdraengt), "Bye{VERDRAENGT} beim neuen Gerät");
    }

    private static async Task<bool> EndeAbwarten(KernFunker f, ByeGrund grund)
    {
        var bis = DateTime.UtcNow.AddSeconds(3);
        while (!f.Enden.Any(e => e.Bye == grund))
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }
}
