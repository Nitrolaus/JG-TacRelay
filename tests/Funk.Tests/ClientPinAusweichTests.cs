// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Die Falle aus dem Umzugsentwurf (TlsAufbau, Erstverbindung): Ein Pin-Fehler an einer Adresse brach sofort mit "Server nicht
/// vertrauenswürdig" ab, ohne die übrigen Hosts des Profils zu versuchen. Im Übergangsprofil (alter und neuer Host) reichte
/// dann ein fremder Dienst unter einem der Namen, und kein Client fand den echten Server. Jetzt versucht die Erstverbindung
/// die übrigen Adressen desselben Versuchs; der Alarm kommt, wenn keine einen gültigen Pin zeigt. Dazu: Ein übernommenes Profil
/// gilt ab dem nächsten Versuch (<see cref="TlsAufbau.ProfilSetzen"/>). Ohne Schannel: Der Handshake je Adresse ist ersetzt,
/// TCP ist echt (Loopback 127.0.0.1 und 127.0.0.2).
/// </summary>
internal static class ClientPinAusweichTests
{
    private static readonly IPAddress Alt = IPAddress.Parse("127.0.0.1");
    private static readonly IPAddress Neu = IPAddress.Parse("127.0.0.2");

    private static Serverprofil Profil(ushort port, params string[] hosts) =>
        new("Test", 2, 0, long.MaxValue / 4, [.. hosts.Select(h => new ProfilHost(h, true, false))], port,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []);

    /// <summary>Zwei Lauscher auf demselben Port, einer je Adresse; <paramref name="neuLauscht"/> = <c>false</c>: nur der alte.</summary>
    private static (TcpListener AltL, TcpListener? NeuL, ushort Port) Lauscher(bool neuLauscht = true)
    {
        for (var versuch = 0; ; versuch++)
        {
            var a = new TcpListener(Alt, 0);
            a.Start();
            var port = (ushort)((IPEndPoint)a.LocalEndpoint).Port;
            if (!neuLauscht) return (a, null, port);
            try
            {
                var n = new TcpListener(Neu, port);
                n.Start();
                return (a, n, port);
            }
            catch (SocketException) when (versuch < 10)
            {
                a.Stop();
            }
        }
    }

    /// <summary>Pin falsch an <paramref name="falsch"/>, sonst richtig; zählt, wo verbunden wurde.</summary>
    private static TlsAufbau Aufbau(Serverprofil profil, Geraeteidentitaet id, List<IPAddress> versucht, params IPAddress[] falsch) =>
        new(profil, id, new TlsAufbauOptionen
        {
            Aufloeser = (name, _, _) => Task.FromResult<IPAddress[]>(name switch
            {
                "alt.test" => [Alt],
                "neu.test" => [Neu],
                _ => throw new SocketException((int)SocketError.HostNotFound),
            }),
            TcpFrist = TimeSpan.FromSeconds(3),
        })
        {
            HandshakeErsatz = (socket, ziel, port, _, _) =>
            {
                lock (versucht) versucht.Add(ziel.Ip);
                if (falsch.Contains(ziel.Ip))
                {
                    socket.Dispose();
                    throw new AufbauException(AufbauFehler.Pin, "Pin passt nicht an " + ziel.Ip + ":" + port);
                }
                return Task.FromResult(new Steuerverbindung(new NetworkStream(socket, ownsSocket: true), ziel.Ip, IPAddress.Loopback, beschreibung: "Test"));
            },
        };

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
    public static async Task KernTls_Erstverbindung_PinFehlerAnEinemHost_NimmtDenAnderen()
    {
        var (altL, neuL, port) = Lauscher();
        try
        {
            using var id = Geraeteidentitaet.Fluechtig();
            var versucht = new List<IPAddress>();
            var aufbau = Aufbau(Profil(port, "alt.test", "neu.test"), id, versucht, Alt);
            await using var v = await aufbau.AufbauenAsync(wiederverbindung: false, CancellationToken.None);
            Pruefe.Gleich(Neu, v.ServerIp, "der Host mit gültigem Pin, obwohl der erste einen fremden zeigte");
            Pruefe.Wahr(versucht.Contains(Alt) && versucht[^1].Equals(Neu), "erst der fremde, dann der echte: " + string.Join(", ", versucht));
            Pruefe.Falsch(aufbau.PinAlarm, "kein Alarm");
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
        }
    }

    [Test]
    public static async Task KernTls_Erstverbindung_KeinGueltigerPin_Alarm()
    {
        using var id = Geraeteidentitaet.Fluechtig();
        // Beide zeigen einen fremden Schlüssel: sofort "Server nicht vertrauenswürdig", wie bisher (4.1).
        var (altL, neuL, port) = Lauscher();
        try
        {
            var versucht = new List<IPAddress>();
            var fehler = await Wirft(() => Aufbau(Profil(port, "alt.test", "neu.test"), id, versucht, Alt, Neu).AufbauenAsync(false, CancellationToken.None));
            Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.PinAlarm }, "beide fremd: Alarm, war " + fehler?.Message);
            Pruefe.Gleich(2, versucht.Count, "beide Hosts versucht");
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
        }

        // Der eine zeigt einen fremden Schlüssel, der andere antwortet gar nicht: Alarm, nicht "nicht erreichbar".
        (altL, neuL, port) = Lauscher(neuLauscht: false);
        try
        {
            var versucht = new List<IPAddress>();
            var fehler = await Wirft(() => Aufbau(Profil(port, "alt.test", "neu.test"), id, versucht, Alt).AufbauenAsync(false, CancellationToken.None));
            Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.PinAlarm } e && e.Message.Contains("Pin passt nicht", StringComparison.Ordinal),
                "fremder Schlüssel und keine Antwort: Alarm mit der Einzelheit, war " + fehler?.GetType().Name + " " + fehler?.Message);

            // Nur ein Host: wie bisher sofort Alarm.
            versucht.Clear();
            fehler = await Wirft(() => Aufbau(Profil(port, "alt.test"), id, versucht, Alt).AufbauenAsync(false, CancellationToken.None));
            Pruefe.Wahr(fehler is AufbauException { Art: AufbauFehler.PinAlarm }, "ein Host, fremder Schlüssel: Alarm");
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
        }
    }

    [Test]
    public static async Task KernTls_Wiederverbindung_UnveraendertFalscherHostZuerst()
    {
        var (altL, neuL, port) = Lauscher();
        try
        {
            using var id = Geraeteidentitaet.Fluechtig();
            var versucht = new List<IPAddress>();
            var aufbau = Aufbau(Profil(port, "alt.test", "neu.test"), id, versucht, Alt);
            await using (var v = await aufbau.AufbauenAsync(wiederverbindung: true, CancellationToken.None))
                Pruefe.Gleich(Neu, v.ServerIp, "beim Wiederverbinden ebenso der andere Host (7.1 Schritt 4)");
            Pruefe.Falsch(aufbau.PinAlarm, "kein Alarm beim Wiederverbinden");
            await using (var v = await aufbau.AufbauenAsync(wiederverbindung: true, CancellationToken.None))
                Pruefe.Gleich(Neu, v.ServerIp, "zweiter Versuch ebenso");
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
        }
    }

    /// <summary>
    /// F1, Gegenprüfung 01.10.2026: Der Aufbau merkt sich den Pin, den der Server der laufenden Verbindung gezeigt hat; daran prüft
    /// die Übernahme, ob ein Profil aus Welcome diesen Server noch enthält.
    /// </summary>
    [Test]
    public static async Task KernTls_LetzterPin_IstDerDerLaufendenVerbindung()
    {
        var (altL, neuL, port) = Lauscher();
        try
        {
            using var id = Geraeteidentitaet.Fluechtig();
            var pinAlt = Fingerabdruck.AusSpki([1]);
            var pinNeu = Fingerabdruck.AusSpki([2]);
            var aufbau = new TlsAufbau(Profil(port, "alt.test", "neu.test"), id, new TlsAufbauOptionen
            {
                Aufloeser = (name, _, _) => Task.FromResult<IPAddress[]>(name == "alt.test" ? [Alt] : [Neu]),
            })
            {
                HandshakeErsatz = (socket, ziel, _, _, _) => Task.FromResult(new Steuerverbindung(new NetworkStream(socket, ownsSocket: true), ziel.Ip, IPAddress.Loopback)
                {
                    Pin = ziel.Ip.Equals(Alt) ? pinAlt : pinNeu,
                }),
            };
            Pruefe.Gleich((Fingerabdruck?)null, aufbau.LetzterPin, "vor der ersten Verbindung unbekannt");
            await using (var v = await aufbau.AufbauenAsync(false, CancellationToken.None))
                Pruefe.Gleich((Fingerabdruck?)(v.ServerIp.Equals(Alt) ? pinAlt : pinNeu), aufbau.LetzterPin, "der Pin dieser Verbindung");
            altL.Stop();
            await using (var v = await aufbau.AufbauenAsync(true, CancellationToken.None))
            {
                Pruefe.Gleich(Neu, v.ServerIp, "der alte antwortet nicht mehr");
                Pruefe.Gleich((Fingerabdruck?)pinNeu, aufbau.LetzterPin, "jetzt der Pin des neuen");
            }
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
        }
    }

    /// <summary>F1: Ein übernommenes Profil gilt ab dem nächsten Versuch, auch beim automatischen Wiederverbinden.</summary>
    [Test]
    public static async Task KernTls_ProfilSetzen_GiltAbDemNaechstenVersuch()
    {
        var (altL, neuL, port) = Lauscher();
        using var anderer = new TcpListener(Neu, 0);
        anderer.Start();
        var anderPort = (ushort)((IPEndPoint)anderer.LocalEndpoint).Port;
        try
        {
            using var id = Geraeteidentitaet.Fluechtig();
            var versucht = new List<IPAddress>();
            var aufbau = Aufbau(Profil(port, "alt.test"), id, versucht);
            await using (var v = await aufbau.AufbauenAsync(false, CancellationToken.None))
                Pruefe.Gleich(Alt, v.ServerIp, "mit dem Setup-Profil zum alten Host");

            var neu = Profil(anderPort, "neu.test") with { ProfilVersion = 3 };
            aufbau.ProfilSetzen(neu);
            Pruefe.Gleich(3u, aufbau.Profil.ProfilVersion, "das Profil für den nächsten Versuch");
            Pruefe.Gleich(anderPort, aufbau.Port, "Port aus dem neuen Profil");
            await using (var v = await aufbau.AufbauenAsync(true, CancellationToken.None))
            {
                Pruefe.Gleich(Neu, v.ServerIp, "beim Wiederverbinden schon der neue Host");
                Pruefe.Gleich(anderPort, (ushort)((IPEndPoint)((NetworkStream)v.Strom).Socket.RemoteEndPoint!).Port, "und der neue Port");
            }
            Pruefe.Wirft<ArgumentNullException>(() => aufbau.ProfilSetzen(null!), "ohne Profil");
        }
        finally
        {
            altL.Stop();
            neuL?.Stop();
            anderer.Stop();
        }
    }
}
