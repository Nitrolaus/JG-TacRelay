// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.ClientKern;
using Funk.Klang.Geraete;
using Funk.Klang.Maschine;
using Funk.Server;
using Funk.Server.Betrieb;
using Funk.Server.Netzzugang;
using Funk.Tests.Maschine;
using Funk.Tests.Verarbeitung;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Der Testzugang von funk-testserver, hier im Prozess: Zulassung wie bei TLS, Besitznachweis des Geräteschlüssels,
/// dann derselbe Weg wie nach einem Handshake. Nur Loopback, kein TLS (unter Windows legte Schannel Schlüssel im
/// Benutzerprofil ab).
/// </summary>
internal sealed class TestzugangLauscher : IAsyncDisposable
{
    private readonly Socket _lauscher;
    private readonly Dienst _dienst;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _annahme;

    public TestzugangLauscher(Dienst dienst)
    {
        _dienst = dienst;
        _lauscher = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _lauscher.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _lauscher.Listen(16);
        Endpunkt = (IPEndPoint)_lauscher.LocalEndPoint!;
        _annahme = Task.Run(AnnehmenAsync);
    }

    public IPEndPoint Endpunkt { get; }

    private async Task AnnehmenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket s;
            try
            {
                s = await _lauscher.AcceptAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => BehandelnAsync(s));
        }
    }

    private async Task BehandelnAsync(Socket s)
    {
        var quelle = Adresse.Aus((IPEndPoint)s.RemoteEndPoint!);
        var zulassung = _dienst.Zentrale.Zulassung;
        if (zulassung.TcpPruefen(quelle, Zeit.MonoMs()) != Zulassungsurteil.Zugelassen)
        {
            s.Dispose();
            return;
        }
        try
        {
            var strom = new NetworkStream(s, ownsSocket: true);
            I.Fingerabdruck geraet;
            using (var frist = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var anfang = new byte[Testzugang.AnfangLaenge];
                await strom.ReadExactlyAsync(anfang, frist.Token);
                var zufall = RandomNumberGenerator.GetBytes(Testzugang.ZufallLaenge);
                await strom.WriteAsync(zufall, frist.Token);
                var signatur = new byte[Testzugang.SignaturLaenge];
                await strom.ReadExactlyAsync(signatur, frist.Token);
                if (!anfang.AsSpan(0, 5).SequenceEqual(Testzugang.Kennung) || !Testzugang.NachweisPruefen(anfang.AsSpan(5), zufall, signatur, out geraet))
                {
                    await strom.DisposeAsync();
                    return;
                }
            }
            await _dienst.NachHandshakeAsync(strom, s, quelle, geraet);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException or InvalidOperationException)
        {
            s.Dispose();
        }
        finally
        {
            zulassung.TcpBeendet(quelle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _lauscher.Dispose();
        try
        {
            await _annahme.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
        }
        _stop.Dispose();
    }
}

/// <summary>Zählt Phasen und Beginn-Klicks und reicht alles an die Brücke weiter.</summary>
internal sealed class ZaehlAnschluss(KlangBruecke innen) : IKlangAnschluss
{
    private readonly Lock _sperre = new();
    private readonly HashSet<(uint, ushort)> _phasen = [];

    public int Phasen
    {
        get
        {
            lock (_sperre) return _phasen.Count;
        }
    }

    public int BeginnKlicks { get; private set; }

    public void Empfangen(in SprachEmpfang empfang)
    {
        lock (_sperre)
        {
            _phasen.Add((empfang.Absender, empfang.Sprechphase));
            if (empfang.BeginnKlick) BeginnKlicks++;
        }
        innen.Empfangen(empfang);
    }

    public void ZeitlinieBeendet(uint absender, ushort sprechphase) => innen.ZeitlinieBeendet(absender, sprechphase);

    public int Samples(ReadOnlySpan<byte> opus) => innen.Samples(opus);

    public void Anbinden(FunkClient client) => innen.Anbinden(client);

    public void Loesen(FunkClient client) => innen.Loesen(client);

    public void AuswahlGeaendert() => innen.AuswahlGeaendert();
}

/// <summary>
/// Ein Funker, wie der WPF-Client ihn zusammensetzt, ohne Fenster und ohne echte Geräte: echte Klangmaschine (APM,
/// RNNoise, Opus, Jitterpuffer, Mischer) mit Geräte-Attrappen an einer gemeinsamen simulierten Uhr, Klangbrücke,
/// FunkClientDienst mit Verbindungsfabrik über den Testzugang.
/// </summary>
internal sealed class KlangFunker : IAsyncDisposable
{
    /// <summary>Startwert des Klick-Rauschens (siehe Konstruktor).</summary>
    private const int KlickSaat = 5;

    private readonly float[] _wiedergabe;

    private KlangFunker(SimUhr uhr, string name)
    {
        Name = name;
        System = new AttrappenSystem();
        // Fester Startwert des Klick-Rauschens: Ohne ihn klang jeder Lauf anders, und bis Klangstufe A dämpfte der eigene Druck-Klick
        // über Loopback und AEC3 den Anfang von Spruch 2 je nach Rauschen verschieden (Gegenprüfung 2c: Saat 5 und 10 fielen
        // immer durch). Seit die Kette den eigenen Druck-Klick aus der Referenz nimmt, besteht der Test mit jeder Saat; 5 bleibt fest.
        Maschine = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = System, UhrUs = uhr.Lesen, EigeneThreads = false, KlickStartwert = KlickSaat });
        Maschine.Fehler += (_, e) => Fehler.Add(e);
        Bruecke = new KlangBruecke(Maschine);
        Zaehler = new ZaehlAnschluss(Bruecke);
        _wiedergabe = new float[System.WiedergabeFormat.Abtastrate / 100 * System.WiedergabeFormat.Kanaele];
    }

    public string Name { get; }

    public AttrappenSystem System { get; }

    public KlangMaschine Maschine { get; }

    public KlangBruecke Bruecke { get; }

    public ZaehlAnschluss Zaehler { get; }

    public FunkClientDienst Dienst { get; private set; } = null!;

    public List<Exception> Fehler { get; } = [];

    /// <summary>Was B hört: die Wiedergabe aller Takte, verschränkt im Geräteformat (48 kHz, stereo).</summary>
    public List<float> Ausgabe { get; } = [];

    public static async Task<KlangFunker> AktivAsync(Testserver t, IPEndPoint zugang, SimUhr uhr, string discord, string name)
    {
        var f = new KlangFunker(uhr, name);
        await f.Maschine.StartenAsync(null, null, CancellationToken.None);
        var v = new VerbindungsEinstellungen { Geraetename = "PC " + name };
        f.Dienst = new FunkClientDienst(() => Verbindungsfabrik.Bauen(v, (_, _) => Task.FromResult(true), f.Zaehler, zugang.ToString()), f.Zaehler);
        await f.Dienst.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => f.Dienst.Lage.Registrierung is not null, 10_000), name + ": Registrierungscode (" + f.Dienst.Lage.Status + ", " + f.Dienst.Lage.Hinweis + ")");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = f.Dienst.Lage.Registrierung!.Code, ["discord"] = discord, ["benutzername"] = name, ["stufe"] = "FUNKER" });
        Pruefe.Wahr(await Warten(() => f.Dienst.Lage.Status == Verbindungsstatus.Warteraum, 5000), name + ": Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Dienst.Lage.Geraet });
        Pruefe.Wahr(await Warten(() => f.Dienst.Lage.IstAktiv && f.Dienst.Lage.Felder.Any(x => x.NetzId == 2 && x.Senden), 10_000), name + ": aktiv mit Feld Kommando");
        return f;
    }

    /// <summary>Ein Takt von 10 ms: Wiedergabe ziehen (und mitschreiben), Loopback, Mikrofon, zustellen.</summary>
    public void Takt(ReadOnlySpan<float> mikrofon)
    {
        System.Wiedergabe?.Ziehen(_wiedergabe);
        Ausgabe.AddRange(_wiedergabe);
        System.Loopback?.Liefern(_wiedergabe);
        System.Aufnahme?.Liefern(mikrofon);
        Maschine.Zustellen();
    }

    public static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = Environment.TickCount64 + ms;
        while (!bedingung())
        {
            if (Environment.TickCount64 > bis) return false;
            await Task.Delay(5);
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Dienst.DisposeAsync();
        Bruecke.Dispose();
        await Maschine.DisposeAsync();
    }
}

/// <summary>
/// Zusammenspiel ohne echte Geräte (Punkt 8 des Integrationsauftrags): Zwei Clients, zusammengesetzt wie der WPF-Client
/// (Klangmaschine, Klangbrücke, FunkClientDienst, Verbindungsfabrik mit Testzugang), gegen den Dienst im selben Prozess
/// auf Loopback. A spricht mit der Sprechtaste zwei Sprüche synthetischer Sprache ins Mikrofon, B muss sie im
/// Mischer-Ausgang haben: Pegel, Dauer und zwei Phasen. Die simulierte Uhr steht, bis B die Pakete von A hat; so ist
/// der Takt der Maschinen unabhängig von der Rechenlast.
/// </summary>
internal static class ZusammenspielKlangTests
{
    private const int Takt = 480;

    private static JsonObject Netz(ushort id, string name) => new()
    {
        ["objekt"] = new JsonObject
        {
            ["id"] = id, ["name"] = name, ["typ"] = "NORMAL", ["reihenfolge"] = id, ["nurTaste"] = false,
            ["ausloeserStandard"] = "TASTE", ["offen"] = "HOEREN_SENDEN", ["standard"] = true,
        },
    };

    [Test]
    public static async Task ZweiClients_EchteKlangmaschine_SpracheKommtBeiBAn()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", Netz(2, "Kommando"));
        await using var zugang = new TestzugangLauscher(t.Dienst);
        var uhr = new SimUhr { Us = 1_000_000 };
        await using var a = await KlangFunker.AktivAsync(t, zugang.Endpunkt, uhr, "300000000000000001", "anna");
        await using var b = await KlangFunker.AktivAsync(t, zugang.Endpunkt, uhr, "300000000000000002", "bert");

        var feld = a.Dienst.Lage.Felder.Single(x => x.NetzId == 2).Feld;
        Pruefe.Wahr(await KlangFunker.Warten(() => a.Dienst.Lage.SendeFelder.SequenceEqual([feld]), 3000),
            "6.9: die Sendeauswahl des Servers (STANDARD: erstes Feld mit senden) gilt ohne Suchen");
        Pruefe.Gleich(null, await a.Dienst.SendeauswahlSetzenAsync([feld], false), "A sendet in Kommando");
        // Kommando steht auf Taste (A10); so setzt es im Client das Funkfeld.
        a.Maschine.Sprachaktivierung = false;
        b.Maschine.Sprachaktivierung = false;
        // Geprüft wird der Weg, nicht die Rauschunterdrückung: RNNoise dämpft diese synthetische Formant-Sprache stark
        // (gemessen: von 770 ms lauten Blöcken bleiben 400), echte Stimmen nicht. APM mit AEC3 und Hochpass läuft, dahinter die Pegelstufe.
        a.Maschine.Rauschfilter = Rauschfilter.Aus;
        var pegel = new List<float>();
        b.Bruecke.NetzPegel += (netz, db) =>
        {
            if (netz == 2) lock (pegel) pegel.Add(db);
        };

        var stille = new float[Takt];
        var endeGesehen = 0;
        var eingang = new List<float>();
        var phasen = new List<(int Von, int Bis)>();
        async Task Lauf(float[] mikrofon, bool taste)
        {
            if (taste) phasen.Add((eingang.Count / Takt, eingang.Count / Takt + mikrofon.Length / Takt));
            eingang.AddRange(mikrofon);
            if (taste) a.Maschine.SprechtasteGedrueckt();
            for (var pos = 0; pos + Takt <= mikrofon.Length; pos += Takt)
            {
                uhr.Us += 10_000;
                a.Takt(mikrofon.AsSpan(pos, Takt));
                // Die Uhr steht, bis B hat, was A bis jetzt gesendet hat (Audio plus die ENDE vergangener Sprüche).
                var soll = a.Bruecke.GesendetePakete + endeGesehen;
                Pruefe.Wahr(await KlangFunker.Warten(() => b.Bruecke.EmpfangenePakete >= soll, 3000),
                    $"B hat {b.Bruecke.EmpfangenePakete} von {soll} Paketen");
                b.Takt(stille);
            }
            if (!taste) return;
            a.Maschine.SprechtasteLosgelassen();
            endeGesehen++;
        }

        var spruch1 = AnbindungSignale.Sprache(48000, 71, -24);
        var spruch2 = AnbindungSignale.Sprache(28800, 72, -24);
        await Lauf(new float[Takt * 50], taste: false);
        await Lauf(spruch1, taste: true);
        await Lauf(new float[Takt * 60], taste: false);
        await Lauf(spruch2, taste: true);
        await Lauf(new float[Takt * 100], taste: false);

        // Auswertung: Pegel je 10-ms-Block im Mikrofon von A und im Mischer-Ausgang von B (stereo, Panorama Mitte). Den
        // Verzug misst jeder Spruch für sich: Der Jitterpuffer beginnt mit 80 ms und senkt sein Ziel ab der zweiten Phase.
        var ausgabe = b.Ausgabe.ToArray();
        var ein = BlockDb([.. eingang], 1);
        var aus = BlockDb(ausgabe, 2);
        Console.WriteLine($"  Zusammenspiel: A sendete {a.Bruecke.GesendetePakete} Pakete, B empfing {b.Bruecke.EmpfangenePakete} " +
            $"(abgewiesen {b.Bruecke.AbgewiesenePakete}), Phasen {b.Zaehler.Phasen}");
        Pruefe.Gleich(2, b.Zaehler.Phasen, "zwei Sprechphasen kamen bei B an");
        PruefeWert.Mindestens(40, a.Bruecke.GesendetePakete, "A sendete Opus-Pakete");
        var drinnen = new bool[aus.Length];
        var bereiche = new List<string>();
        foreach (var (von, bis) in phasen)
        {
            var verzug = BesterVerzug(ein, aus, von, bis, 40);
            Pruefe.Wahr(verzug is >= 2 and <= 30, $"Verzug {verzug * 10} ms (Kette, Opus, Jitterpuffer)");
            var aktivEin = 0;
            var aktivAus = 0;
            double energieEin = 0, energieAus = 0;
            for (var i = von; i < bis; i++)
            {
                if (ein[i] > -45)
                {
                    aktivEin++;
                    energieEin += Math.Pow(10, ein[i] / 10);
                }
                if (i + verzug < aus.Length && aus[i + verzug] > -45)
                {
                    aktivAus++;
                    energieAus += Math.Pow(10, aus[i + verzug] / 10);
                }
            }
            // Dazu gehören die Klicks: der am Beginn und der 100 ms nach dem Ende, seit Stufe 2b ein Rauschen von 90 ms.
            for (var i = Math.Max(0, von + verzug - 3); i < Math.Min(aus.Length, bis + verzug + 20); i++) drinnen[i] = true;
            bereiche.Add((von + verzug) * 10 + "-" + (bis + verzug) * 10);
            var pegelEin = 10 * Math.Log10(energieEin / Math.Max(1, aktivEin));
            var pegelAus = 10 * Math.Log10(energieAus / Math.Max(1, aktivAus));
            Console.WriteLine($"  Zusammenspiel: Spruch {von * 10} bis {bis * 10} ms: laut bei A {aktivEin * 10} ms mit {pegelEin:0.0} dBFS, " +
                $"bei B {aktivAus * 10} ms mit {pegelAus:0.0} dBFS, Verzug Mikrofon A bis Ausgang B {verzug * 10} ms");
            PruefeWert.Nahe(aktivEin, aktivAus, Math.Max(4, 0.15 * aktivEin), "Dauer der Sprache bei B wie bei A (Blöcke zu 10 ms)");
            // Die Pegelstufe hebt eine eher leise Stimme an (bis 2c die AGC2, gemessen um 8 dB); leiser als bei A darf sie nicht ankommen.
            PruefeWert.Mindestens(pegelEin - 3, pegelAus, "Pegel bei B mindestens wie bei A");
            PruefeWert.Hoechstens(-3, pegelAus, "und nicht übersteuert");
        }
        var (draussenDb, draussenMs) = (-120.0, -1);
        for (var i = 0; i < aus.Length; i++)
        {
            if (!drinnen[i] && aus[i] > draussenDb) (draussenDb, draussenMs) = (aus[i], i * 10);
        }
        PruefeWert.Hoechstens(-45, draussenDb, $"außerhalb der Sprüche Ruhe bei B (lautester Block bei {draussenMs} ms, Sprüche bei B "
            + string.Join(", ", bereiche) + " ms)");
        PruefeWert.Hoechstens(1.0, ausgabe.Max(MathF.Abs), "nie über Vollaussteuerung");
        PruefeWert.Mindestens(40, a.Bruecke.GesendetePakete, "A sendete Opus-Pakete");
        Pruefe.Gleich(0L, b.Bruecke.AbgewiesenePakete, "B nahm alles an");
        lock (pegel) Pruefe.Wahr(pegel.Count > 10 && pegel.Max() > -40, $"Empfangspegel je Netz für die Kachel (A18): {pegel.Count} Meldungen");
        Pruefe.Wahr(a.Fehler.Count == 0 && b.Fehler.Count == 0, "keine Fehler der Maschinen: " + string.Join(" | ", a.Fehler.Concat(b.Fehler).Select(e => e.Message)));
    }

    /// <summary>Pegel je 10-ms-Block in dBFS (verschränkte Kanäle zusammen).</summary>
    private static double[] BlockDb(float[] werte, int kanaele)
    {
        var block = Takt * kanaele;
        var db = new double[werte.Length / block];
        for (var i = 0; i < db.Length; i++) db[i] = AnbindungSignale.PegelDb(werte.AsSpan(i * block, block));
        return db;
    }

    /// <summary>Verzug in Blöcken, bei dem die Hüllkurven (Energie, ab -60 dBFS) im Spruch von bis bis am besten übereinanderliegen.</summary>
    private static int BesterVerzug(double[] ein, double[] aus, int von, int bis, int max)
    {
        static double E(double db) => db < -60 ? 0 : Math.Pow(10, db / 10);
        var bester = 0;
        var besteSumme = double.MinValue;
        for (var v = 0; v <= max; v++)
        {
            double summe = 0;
            for (var i = von; i < bis && i < ein.Length && i + v < aus.Length; i++) summe += Math.Sqrt(E(ein[i]) * E(aus[i + v]));
            if (summe > besteSumme)
            {
                besteSumme = summe;
                bester = v;
            }
        }
        return bester;
    }
}
