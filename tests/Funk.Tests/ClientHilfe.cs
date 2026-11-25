// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;

namespace Funk.Tests;

/// <summary>
/// Hält den jeweils laufenden Testserver, damit Clients nach einem Neustart den neuen erreichen. Verbindungen gehen
/// prozessintern am TLS-Handshake vorbei über <c>Dienst.NachHandshakeAsync</c> (unter Windows schreibt so nichts ins
/// Benutzerprofil). <see cref="HartTrennen"/> schließt die Server-Seite aller Verbindungen wie ein beendeter Prozess.
/// </summary>
internal sealed class ServerHalter
{
    private readonly ConcurrentBag<Socket> _serverSeiten = [];

    public Testserver? Server { get; set; }

    public DelegatAufbau Aufbau(Geraeteidentitaet identitaet, IPAddress lokal) => new(async (wiederverbindung, abbruch) =>
    {
        var t = Server ?? throw new AufbauException(AufbauFehler.Netz, "Server läuft nicht");
        var dienst = t.Dienst;
        using var lauscher = new TcpListener(IPAddress.Loopback, 0);
        lauscher.Start();
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            client.Bind(new IPEndPoint(lokal, 0));
            var annahme = lauscher.AcceptSocketAsync(abbruch);
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)lauscher.LocalEndpoint).Port, abbruch);
            var serverSeite = await annahme;
            _serverSeiten.Add(serverSeite);
            var quelle = Adresse.Aus((IPEndPoint)serverSeite.RemoteEndPoint!);
            _ = Task.Run(async () =>
            {
                try
                {
                    await dienst.NachHandshakeAsync(new NetworkStream(serverSeite, ownsSocket: true), serverSeite, quelle, identitaet.Fingerabdruck);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or IOException or InvalidOperationException or NullReferenceException)
                {
                    // Dienst beendet, während die Verbindung lief.
                }
            }, CancellationToken.None);
            return new Steuerverbindung(new NetworkStream(client, ownsSocket: true), IPAddress.Loopback, lokal, beschreibung: "prozessintern");
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            client.Dispose();
            throw new AufbauException(AufbauFehler.Netz, "prozessintern nicht erreichbar", ex);
        }
    });

    /// <summary>Wie ein abgestürzter Serverprozess: alle Server-Seiten schließen, ohne Bye.</summary>
    public void HartTrennen()
    {
        while (_serverSeiten.TryTake(out var s))
        {
            try
            {
                s.Close(0);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}

/// <summary>Ein empfangenes Sprachpaket, kopiert aus <see cref="SprachEmpfang"/>.</summary>
internal sealed record EmpfangenesPaket(uint Absender, ushort Phase, uint Frame, Kennzeichen Kennzeichen, Spruchart Art, ushort[] Netze,
    byte[] Opus, bool NeueZeitlinie, bool Fortsetzung, bool Ende, Verbindungsweg Weg);

/// <summary>Sammelt empfangene Sprache für Prüfungen.</summary>
internal sealed class Sammler : ISprachEmpfaenger
{
    private readonly ConcurrentQueue<EmpfangenesPaket> _pakete = new();

    public IReadOnlyCollection<EmpfangenesPaket> Pakete => _pakete;

    public int Beendet;

    /// <summary>Stellt eine fehlerhafte Audio-Schicht nach: nach dem Mitschreiben wirft jeder Aufruf.</summary>
    public volatile bool Werfen;

    public void Empfangen(in SprachEmpfang e)
    {
        var netze = new ushort[e.Paket.NetzAnzahl];
        for (var i = 0; i < netze.Length; i++) netze[i] = e.Paket.Netz(i);
        _pakete.Enqueue(new EmpfangenesPaket(e.Absender, e.Sprechphase, e.Paket.FrameNr, e.Paket.Kennzeichen, e.Paket.Art, netze,
            e.Paket.Opus.ToArray(), e.NeueZeitlinie, e.Fortsetzung, e.Ende, e.Weg));
        if (Werfen) throw new InvalidOperationException("Test: die Audio-Schicht wirft");
    }

    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
        Interlocked.Increment(ref Beendet);
        if (Werfen) throw new InvalidOperationException("Test: die Audio-Schicht wirft");
    }

    public void Leeren() => _pakete.Clear();

    public List<EmpfangenesPaket> Von(uint absender) => [.. _pakete.Where(p => p.Absender == absender)];

    public async Task<bool> WartenAsync(Func<IReadOnlyCollection<EmpfangenesPaket>, bool> bedingung, TimeSpan frist)
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

/// <summary>Ein Funker auf Basis von <see cref="FunkClient"/> (Funk.ClientKern) für Zusammenspieltests.</summary>
internal sealed class KernFunker : IAsyncDisposable
{
    /// <summary>Opus-TOC 0xF8: CELT FB 20 ms, mono, ein Frame (5.13); der Rest ist beliebig.</summary>
    public static readonly byte[] Opus = [0xF8, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

    private readonly ConcurrentQueue<PushEreignis> _pushes = new();

    private KernFunker(FunkClient client, Geraeteidentitaet identitaet, Sammler sammler, string name)
    {
        Client = client;
        Identitaet = identitaet;
        Empfang = sammler;
        Name = name;
        client.PushErhalten += (_, p) => _pushes.Enqueue(p);
        client.SitzungBeendet += (_, e) => Enden.Enqueue(e);
    }

    /// <summary>Alle Sitzungsenden und gescheiterten Aufbauten, in der Reihenfolge.</summary>
    public ConcurrentQueue<Sitzungsende> Enden { get; } = new();

    public FunkClient Client { get; }
    public Geraeteidentitaet Identitaet { get; }
    public Sammler Empfang { get; }
    public string Name { get; }
    public uint KontoId => Client.Lage.KontoId;
    public IReadOnlyCollection<PushEreignis> Pushes => _pushes;

    public void PushesLeeren() => _pushes.Clear();

    /// <summary>
    /// Neues Gerät bis AKTIV/UDP über die Client-Bibliothek: Hello, Code, Verknüpfen per Admin-Werkzeug mit Rückfrage
    /// (der Client sagt ja), Freigabe, Sync, UDP-Bindung.
    /// </summary>
    public static async Task<KernFunker> AktivAsync(ServerHalter halter, string discord, string benutzer, string stufe, IPAddress lokal,
        bool udpSperre = false, TimeSpan? probeAbstand = null, TimeSpan? probeDauer = null, bool aktivErwarten = true)
    {
        var t = halter.Server!;
        var identitaet = Geraeteidentitaet.Fluechtig();
        var sammler = new Sammler();
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = halter.Aufbau(identitaet, lokal),
            Identitaet = identitaet,
            Geraetename = "PC " + benutzer,
            ClientName = "Kerntest",
            ClientVersion = "1.0.0",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = sammler,
            Zufall = () => 0.0,
            TunnelProbeAbstand = probeAbstand ?? TimeSpan.FromSeconds(30),
            TunnelProbeDauer = probeDauer ?? TimeSpan.FromSeconds(10),
        })
        {
            UdpSperre = udpSperre,
        };
        var f = new KernFunker(client, identitaet, sammler, benutzer);
        var code = new TaskCompletionSource<Registrierungscode>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.CodeErhalten += (_, c) => code.TrySetResult(c);
        client.Starten();
        var c = await code.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = c.Code, ["discord"] = discord, ["benutzername"] = benutzer, ["stufe"] = stufe });
        Pruefe.Wahr(await client.WartenAufAsync(z => z == ClientZustand.Warteraum, TimeSpan.FromSeconds(5)), benutzer + ": WARTERAUM nach der Verknüpfung");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        if (aktivErwarten) Pruefe.Wahr(await client.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(10)), benutzer + ": AKTIV/UDP, war " + client.Zustand + ", " + client.Messwerte.Stand());
        else Pruefe.Wahr(await client.WartenAufAsync(z => z is ClientZustand.AktivUdpBinden or ClientZustand.AktivUdp or ClientZustand.AktivTunnel, TimeSpan.FromSeconds(10)), benutzer + ": AKTIV, war " + client.Zustand);
        return f;
    }

    /// <summary>Wartet auf einen Push des Typs <typeparamref name="T"/>, der <paramref name="passt"/> erfüllt.</summary>
    public async Task<T?> PushAsync<T>(Func<T, bool>? passt, TimeSpan frist) where T : class
    {
        var bis = DateTime.UtcNow + frist;
        while (DateTime.UtcNow < bis)
        {
            foreach (var p in _pushes)
                if (p.Body is T b && (passt is null || passt(b))) return b;
            await Task.Delay(10);
        }
        return null;
    }

    /// <summary>Eine kurze Phase: Audio-Frames im 20-ms-Takt (wie ein Encoder), dann ENDE (dreimal).</summary>
    public async Task<bool> SpruchAsync(Sendeziel ziel, bool va = false, int frames = 3)
    {
        if (!Client.Sprechen.Beginnen(ziel, va)) return false;
        for (var i = 0; i < frames; i++)
        {
            if (i > 0) await Task.Delay(20);
            Client.Sprechen.Senden(Opus);
        }
        Client.Sprechen.Beenden();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Identitaet.Dispose();
    }
}
