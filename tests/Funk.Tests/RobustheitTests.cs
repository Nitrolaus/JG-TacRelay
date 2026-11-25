// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Server.Netzzugang;
using Funk.Server.Sprachpfad;

namespace Funk.Tests;

/// <summary>
/// Gegenprüfung von Server und Client: keine Verstärkung an unbestätigte Adressen (5.10, 7.5), Tunnel nur mit der eigenen
/// Sitzungs-ID (5.9 Schritt 3), Vorfilter und Zulassung unter einer Flut vieler Präfixe, Bye{WARTUNG} an alle
/// Verbindungen (7.2), und dass Ausnahmen in Threads und im Code der Anwendung weder Prozess noch Sitzung beenden.
/// </summary>
internal static class RobustheitTests
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

    /// <summary>
    /// 5.10 und 7.5: Eine PFAD_PRUEFUNG (48 Byte) an eine noch unbestätigte Adresse ist nie größer als das auslösende
    /// Datagramm. Ein ENDE ohne Opus (41 bzw. 43 Byte) von einer neuen Adresse löst deshalb keine Prüfung aus, ein
    /// KEEPALIVE (48 Byte) von derselben Adresse schon.
    /// </summary>
    [Test]
    public static async Task Robust_KleinesPaketLoestKeinePfadpruefungAus()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2));
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        a.UdpVerbinden();
        await a.UdpBindenAsync();

        a.UdpPortWechseln();
        a.Versiegeln(PaketTyp.SpracheHoch, EndeKlartext([2], Zielart.Netz), out var ende43);
        a.Versiegeln(PaketTyp.SpracheHoch, EndeKlartext([], Zielart.Durchsage), out var ende41);
        Pruefe.Gleich(43, ende43.Length, "ENDE an ein Netz ohne Opus");
        Pruefe.Gleich(41, ende41.Length, "ENDE einer Durchsage an alle ohne Opus");
        a.RohUdpSenden(ende43);
        a.RohUdpSenden(ende41);
        var antwort = await a.UdpEmpfangenAsync(TimeSpan.FromMilliseconds(600));
        Pruefe.Wahr(antwort is null, "keine PFAD_PRUEFUNG auf ein Datagramm unter 48 Byte (war " + antwort?.Kopf.Typ + ")");

        var keepalive = new byte[Steuerpakete.Laenge];
        new Keepalive(7, 70).Schreiben(keepalive);
        a.UdpSenden(PaketTyp.Keepalive, keepalive);
        Pruefe.Wahr(await a.PfadPruefungBeantwortenAsync(TimeSpan.FromSeconds(2)), "KEEPALIVE (48 Byte) löst die Pfadprüfung aus");
        var status = (await a.ErwarteAsync(Nachrichtentyp.UdpStatus, TimeSpan.FromSeconds(3))).Json<UdpStatus>();
        Pruefe.Gleich(UdpZustand.Gebunden, status.Zustand, "danach umgebunden");
    }

    private static byte[] EndeKlartext(ushort[] ziele, Zielart art)
    {
        var k = new byte[Sprachkanal.MaxKlartext];
        var n = SpracheHoch.Schreiben(k, 100, 9, Kennzeichen.Ende, art, ziele, []);
        return k[..n];
    }

    /// <summary>5.9 Schritt 3: Ein Tunnelrahmen mit der Sitzungs-ID einer anderen Sitzung heißt PROTOKOLL und Trennung.</summary>
    [Test]
    public static async Task Robust_TunnelMitFremderSitzungsIdTrennt()
    {
        await using var t = await Testserver.StartenAsync();
        await using var a = await t.AktivAsync("111111111111111111", "anna");
        await using var b = await t.AktivAsync("222222222222222222", "bert");
        var klartext = new byte[Steuerpakete.Laenge];
        new Keepalive(1, 1).Schreiben(klartext);
        a.Versiegeln(PaketTyp.Keepalive, klartext, out var d);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(4), b.SitzungsId);
        await a.RohAsync((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, d);
        var f = (await a.ErwarteAsync(Nachrichtentyp.Fehler, TimeSpan.FromSeconds(3))).Json<Fehler>();
        Pruefe.Gleich(Fehlercode.Protokoll, f.Code, "Fehler nach fremder Sitzungs-ID im Tunnel");
        await a.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
        Pruefe.Falsch(b.Getrennt, "die fremde Sitzung bleibt unberührt");
    }

    /// <summary>
    /// Vorfilter (5.9 Schritt 4) bei voller Tabelle: neue Präfixe gehen ohne Vorfilter weiter (sonst schaffte niemand mehr
    /// eine Erstbindung oder ein NAT-Rebinding); bekannte Präfixe bleiben begrenzt; ruhende fallen nach 10 s heraus.
    /// </summary>
    [Test]
    public static void Robust_VorfilterVollSperrtNeuePraefixeNicht()
    {
        var f = new Vorfilter(max: 4);
        var t = Zeit.Takte();
        for (var i = 1; i <= 4; i++) Pruefe.Wahr(f.Nehmen(new Praefix((UInt128)i, false), t), "Präfix " + i);
        Pruefe.Gleich(4, f.Anzahl, "vier Präfixe verfolgt");
        Pruefe.Wahr(f.Nehmen(new Praefix(99, false), t), "neues Präfix bei voller Tabelle kommt durch");
        Pruefe.Gleich(1L, f.Uebergelaufen, "als Überlauf gezählt");
        Pruefe.Gleich(4, f.Anzahl, "die Tabelle wächst nicht");

        var bekannt = new Praefix(1, false);
        var angenommen = 1;
        while (angenommen < 1000 && f.Nehmen(bekannt, t)) angenommen++;
        Pruefe.Gleich(200, angenommen, "ein bekanntes Präfix bleibt beim Stoß von 200 begrenzt");

        f.Pflegen(t + Vorfilter.RuheTakte + 1);
        Pruefe.Gleich(0, f.Anzahl, "ruhende Präfixe fallen heraus");
        Pruefe.Wahr(f.Nehmen(new Praefix(5, false), t + Vorfilter.RuheTakte + 1), "danach wieder mit Vorfilter");
        Pruefe.Gleich(1, f.Anzahl, "und verfolgt");
    }

    /// <summary>
    /// Zulassung unter einer Flut vieler IPv6-/64 (4.8): über der Grenze der verfolgten Präfixe kostet jedes neue Präfix
    /// nur einen Topf, keine Kopie der ganzen Tabelle; ein Präfix mit offener Verbindung behält seinen geleerten Topf.
    /// </summary>
    [Test]
    public static void Robust_ZulassungUnterPraefixflutOhneKopie()
    {
        var z = new Zulassung(new GrenzenKonfiguration(), new Vorrangspur(new byte[32]), null, Zeit.MonoMs());
        var opfer = new Adresse((UInt128)0x7F00_0001, 40000, false);
        var jetzt = Zeit.MonoMs();
        for (var i = 0; i < 5; i++) Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(opfer, jetzt), "Stoß " + i);
        Pruefe.Gleich(Zulassungsurteil.PraefixTopfLeer, z.TcpPruefen(opfer, jetzt), "Topf des Präfixes leer");

        static Adresse V6(int i) => new(((UInt128)0x2001_0db8_0000_0000UL + (ulong)i) << 64 | 1, 50000, true);
        for (var i = 0; i < Zulassung.MaxPraefixe; i++)
        {
            z.TcpPruefen(V6(i), jetzt);
            z.TcpBeendet(V6(i));
        }
        Pruefe.Gleich(Zulassung.MaxPraefixe, z.VerfolgtePraefixe, "Tabelle voll");
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        var uhr = Stopwatch.StartNew();
        for (var i = 0; i < 2000; i++)
        {
            z.TcpPruefen(V6(Zulassung.MaxPraefixe + i), jetzt);
            z.TcpBeendet(V6(Zulassung.MaxPraefixe + i));
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Wahr(bytes < 20L * 1024 * 1024, $"2000 neue Präfixe über der Grenze allokieren {bytes / 1024} KiB (früher je Präfix eine Kopie aller 65.536 Schlüssel)");
        Pruefe.Wahr(z.VerfolgtePraefixe <= Zulassung.MaxPraefixe, "die Tabelle bleibt begrenzt");
        Pruefe.Gleich(Zulassungsurteil.PraefixTopfLeer, z.TcpPruefen(opfer, jetzt), "Präfix mit offenen Verbindungen behält seinen leeren Topf (" + uhr.ElapsedMilliseconds + " ms)");
    }

    /// <summary>7.2: Bye{WARTUNG, wiederInMs} geht an alle Verbindungen, auch in REGISTRIERUNG und WARTERAUM.</summary>
    [Test]
    public static async Task Robust_WartungErreichtAuchRegistrierungUndWarteraum()
    {
        var t = await Testserver.StartenAsync();
        try
        {
            await using var reg = await t.VerbindenAsync(null, new IPAddress([127, 0, 9, 1]));
            Pruefe.Gleich(WelcomeStatus.Unbekannt, (await reg.HelloAsync()).Status, "REGISTRIERUNG");

            await using var warte = await t.VerbindenAsync(null, new IPAddress([127, 0, 9, 2]));
            await warte.HelloAsync();
            var code = await warte.RegistrierenAsync("PC Wartend");
            var verknuepfen = t.Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = "333333333333333333", ["benutzername"] = "wanda" });
            var rueckfrage = await warte.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
            await warte.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, rueckfrage.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
            Pruefe.Wahr((await verknuepfen).Ok, "verknuepfen");
            await warte.ErwarteAsync(Nachrichtentyp.GeraetStatus);

            await t.GeordnetBeendenAsync(4000);
            foreach (var (f, name) in new[] { (reg, "REGISTRIERUNG"), (warte, "WARTERAUM") })
            {
                var bye = (await f.ErwarteAsync(Nachrichtentyp.Bye, TimeSpan.FromSeconds(3))).Json<Bye>();
                Pruefe.Gleich(ByeGrund.Wartung, bye.Grund, name + ": Bye WARTUNG");
                Pruefe.Gleich(4000u, bye.WiederInMs, name + ": wiederInMs");
                await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(3));
            }
        }
        finally
        {
            await t.DisposeAsync();
        }
    }

    /// <summary>
    /// Der Sende-Thread des Servers überlebt eine unerwartete Ausnahme (sonst endete der ganze Prozess): das Paket gilt als
    /// Sendefehler, der Fehler geht ans Logbuch, das nächste Paket geht hinaus.
    /// </summary>
    [Test]
    public static async Task Robust_AusgangUeberlebtAusnahme()
    {
        using var ziel = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        ziel.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var quelle = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        quelle.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var fehler = new ConcurrentQueue<Exception>();
        var ausgang = new Ausgang([quelle], 1000, 26, fehler.Enqueue);
        ausgang.Starten();
        try
        {
            var kaputt = ausgang.Leihen();
            kaputt.Laenge = 48;
            kaputt.Ziel = ziel.LocalEndPoint!.Serialize();
            kaputt.Socket = 5; // gibt es nicht
            ausgang.Einreihen(kaputt);
            var gut = ausgang.Leihen();
            gut.Laenge = 48;
            gut.Puffer.AsSpan(0, 48).Fill(0x5A);
            gut.Ziel = ziel.LocalEndPoint!.Serialize();
            gut.Socket = 0;
            ausgang.Einreihen(gut);

            using var frist = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var puffer = new byte[64];
            var n = await ziel.ReceiveAsync(puffer, SocketFlags.None, frist.Token);
            Pruefe.Gleich(48, n, "das nächste Paket ging hinaus");
            Pruefe.Gleich((byte)0x5A, puffer[0], "Inhalt");
            Pruefe.Wahr(Interlocked.Read(ref ausgang.Sendefehler) >= 1, "Sendefehler gezählt");
            Pruefe.Gleich(1, fehler.Count, "ein Fehlerbericht");
        }
        finally
        {
            ausgang.Dispose();
        }
    }

    /// <summary>
    /// Client: Eine Audio-Schicht und Ereignisbehandler, die werfen, beenden weder den Prozess (Empfangs-Thread) noch die
    /// Sitzung (Leseschleife, auch im Tunnel). Die Ausnahmen gehen an HintergrundFehler, Sprache und Steuerkanal laufen weiter.
    /// </summary>
    [Test]
    public static async Task Robust_ClientUeberlebtWerfendeAnwendung()
    {
        var halter = new ServerHalter();
        await using var t = await Testserver.StartenAsync();
        halter.Server = t;
        await t.AdminOk("netz-setzen", Netz(2));
        await using var a = await KernFunker.AktivAsync(halter, "444444444444444444", "anna", "FUNKER", new IPAddress([127, 0, 9, 11]));
        await using var b = await KernFunker.AktivAsync(halter, "555555555555555555", "bert", "FUNKER", new IPAddress([127, 0, 9, 12]));
        var fehler = 0;
        b.Client.HintergrundFehler += (_, _) => Interlocked.Increment(ref fehler);
        b.Client.PushErhalten += (_, _) => throw new InvalidOperationException("Test: PushErhalten wirft");
        b.Empfang.Werfen = true;

        // UDP: der Empfangs-Thread ruft die Audio-Schicht auf.
        Pruefe.Wahr(await a.SpruchAsync(Sendeziel.Netz(2), frames: 5), "A spricht");
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Count(x => !x.Ende) >= 5, TimeSpan.FromSeconds(3)), "B empfängt alle 5 Frames trotz werfender Audio-Schicht");
        Pruefe.Wahr(Volatile.Read(ref fehler) >= 5, "Ausnahmen gehen an HintergrundFehler (" + fehler + ")");
        Pruefe.Gleich(ClientZustand.AktivUdp, b.Client.Zustand, "B bleibt AKTIV/UDP");

        // Steuerkanal: Pushes werfen beim Behandler, die Leseschleife liest weiter (die Antwort kommt an).
        var hoeren = await b.Client.HoerenSetzenAsync(2, true).WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Wahr(hoeren.Ok, "Anfrage nach werfenden Pushes beantwortet");

        // Tunnel: dieselbe Audio-Schicht auf der Leseschleife.
        b.Client.TunnelNutzer(true);
        Pruefe.Wahr(await b.Client.WartenAufAsync(z => z == ClientZustand.AktivTunnel, TimeSpan.FromSeconds(3)), "B im Tunnel");
        await Task.Delay(300);
        b.Empfang.Leeren();
        Pruefe.Wahr(await a.SpruchAsync(Sendeziel.Netz(2), frames: 5), "A spricht erneut");
        Pruefe.Wahr(await b.Empfang.WartenAsync(p => p.Count(x => !x.Ende && x.Weg == Verbindungsweg.Tunnel) >= 5, TimeSpan.FromSeconds(3)),
            "B empfängt im Tunnel trotz werfender Audio-Schicht");
        var nochmal = await b.Client.HoerenSetzenAsync(2, true).WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Wahr(nochmal.Ok, "Leseschleife lebt nach Ausnahmen im Tunnelempfang");
        Pruefe.Gleich(ClientZustand.AktivTunnel, b.Client.Zustand, "B bleibt AKTIV/TUNNEL");
        Pruefe.Gleich(1, b.Client.Aktivierungen, "keine neue Sitzung nötig");
    }
}
