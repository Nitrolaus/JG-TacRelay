// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;

namespace Funk.Testclient;

/// <summary>Einstellungen eines Laufs.</summary>
internal sealed record Laufoptionen(int Funker, double Sekunden, int OpusBytes, int Parallel, double Aufwaermen = 3, int Gruppen = 1)
{
    public int Takte => (int)Math.Round(Sekunden * 50);
}

/// <summary>
/// Der Lasttest: N Funker anmelden (Admin-Schritte über funk-admin), Netze anlegen, je Szenario die Felder setzen,
/// Sprecher im 20-ms-Takt senden lassen und je Empfänger Verlust, Laufzeit, Jitter, Pakete/s und Bandbreite messen; am
/// Server CPU und Speicher des Prozesses sowie die Relais-Messwerte.
/// </summary>
internal sealed class Lasttest(Laufoptionen optionen, AdminWerkzeug admin, Process? server, Func<int, Geraeteidentitaet, IVerbindungsaufbau> aufbau, TextWriter aus)
{
    private readonly List<Funker> _funker = [];

    public async Task<List<Szenarioergebnis>> LaufenAsync(IEnumerable<char> szenarien)
    {
        var ergebnisse = new List<Szenarioergebnis>();
        szenarien = [.. szenarien];
        try
        {
            await NetzeAnlegenAsync(szenarien.Any(Szenario.MitKanaelen)).ConfigureAwait(false);
            await AnmeldenAsync().ConfigureAwait(false);
            if (optionen.Aufwaermen > 0)
            {
                // Aufwärmen (JIT, Puffer, Pools) mit dem Verkehr von B, nicht gewertet.
                var w = await SzenarioAsync(Szenario.Bauen('B', optionen.Funker), 250, optionen.Aufwaermen).ConfigureAwait(false);
                aus.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aufwärmen {optionen.Aufwaermen} s (Verkehr wie B, nicht gewertet): Verlust {w.VerlustProzent:F3} %"));
            }
            byte lauf = 1;
            foreach (var k in szenarien)
            {
                var e = await SzenarioAsync(Szenario.Bauen(k, optionen.Funker), lauf++).ConfigureAwait(false);
                aus.WriteLine(e.Zeile());
                ergebnisse.Add(e);
            }
        }
        finally
        {
            await Task.WhenAll(_funker.Select(f => f.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        return ergebnisse;
    }

    private async Task NetzeAnlegenAsync(bool mitKanaelen)
    {
        foreach (var netz in Szenario.AlleNetze())
        {
            var id = netz.ToString(CultureInfo.InvariantCulture);
            await admin.PflichtAsync("netz-setzen", "--id", id, "--name", "Last " + id, "--offen", "HOEREN_SENDEN", "--reihenfolge", id).ConfigureAwait(false);
        }
        aus.WriteLine("Netze angelegt: " + string.Join(", ", Szenario.AlleNetze()));
        if (!mitKanaelen) return;
        // J und K (6.16): zwei Kanalnetze mit Ruhe und Ch. 1 bis 20; ab hier gilt das Versionstor 0.6.0 (der Testclient meldet 1.0.0).
        var kanaele = "Ruhe," + string.Join(",", Enumerable.Range(1, Szenario.ChAnzahl).Select(c => "Ch " + c.ToString(CultureInfo.InvariantCulture)));
        foreach (var netz in new[] { Szenario.KanalnetzSprechen, Szenario.KanalnetzWechsel })
        {
            var id = netz.ToString(CultureInfo.InvariantCulture);
            await admin.PflichtAsync("netz-setzen", "--id", id, "--name", "Last K" + id, "--offen", "HOEREN_SENDEN", "--reihenfolge", id, "--kanaele", kanaele)
                .ConfigureAwait(false);
        }
        aus.WriteLine("Kanalnetze angelegt: " + Szenario.KanalnetzSprechen + ", " + Szenario.KanalnetzWechsel + " (je Ruhe und Ch. 1 bis " + Szenario.ChAnzahl + ")");
    }

    /// <summary>
    /// K: Jeder Funker wechselt einmal je Sekunde den Ch. im Kanalnetz <paramref name="netz"/> (FeldSetzen auf sein Feld dort), über die
    /// Sekunde verteilt. Gemessen: Antwortzeit je Wechsel (Anfrage bis Antwort, also mit dem Commit am Server) und wie viele scheiterten.
    /// </summary>
    private async Task<(List<double> Ms, int Fehler)> ChWechselnAsync(ushort netz, CancellationToken stop)
    {
        var ms = new List<double>();
        var fehler = 0;
        var runde = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var beginn = Stopwatch.GetTimestamp();
                runde++;
                for (var i = 0; i < _funker.Count && !stop.IsCancellationRequested; i++)
                {
                    var f = _funker[i];
                    var feld = f.Client.Lage.Mitgliedschaften?.Felder.FirstOrDefault(m => Kanalrechnung.Zerlegen(m.NetzId, out var n, out _) && n == netz);
                    if (feld is null)
                    {
                        fehler++;
                    }
                    else
                    {
                        var t0 = Stopwatch.GetTimestamp();
                        var a = await f.Client.FeldSetzenAsync(feld.Feld, Kanalrechnung.Id(netz, (byte)(1 + (i + runde) % Szenario.ChAnzahl))).ConfigureAwait(false);
                        if (a.Ok) ms.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                        else fehler++;
                    }
                    var warten = (beginn + (i + 1) * Stopwatch.Frequency / _funker.Count - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                    if (warten > 0) await Task.Delay((int)warten, stop).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        return (ms, fehler);
    }

    /// <summary>Anmeldung aller Funker: Hello, Code, verknuepfen (Rückfrage ja), freigeben, Sync, UDP-Bindung.</summary>
    private async Task AnmeldenAsync()
    {
        var uhr = Stopwatch.StartNew();
        using var parallel = new SemaphoreSlim(optionen.Parallel);
        var aufgaben = new List<Task>();
        for (var i = 0; i < optionen.Funker; i++)
        {
            var identitaet = Geraeteidentitaet.Fluechtig();
            var f = new Funker(i, optionen.Funker, aufbau(i, identitaet), identitaet);
            _funker.Add(f);
            aufgaben.Add(AnmeldenAsync(f, parallel));
        }
        await Task.WhenAll(aufgaben).ConfigureAwait(false);
        aus.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{optionen.Funker} Funker angemeldet und AKTIV/UDP in {uhr.Elapsed.TotalSeconds:F1} s"));
    }

    private async Task AnmeldenAsync(Funker f, SemaphoreSlim parallel)
    {
        await parallel.WaitAsync().ConfigureAwait(false);
        try
        {
            f.Client.Starten();
            var code = await f.CodeAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await admin.PflichtAsync("verknuepfen", "--code", code.Code, "--discord", f.Discord, "--benutzername", f.Benutzer,
                "--stufe", f.Index == 0 ? "RAIDLEITER" : "FUNKER").ConfigureAwait(false);
            if (!await f.Client.WartenAufAsync(z => z == ClientZustand.Warteraum, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                throw new InvalidOperationException(f.Benutzer + ": kein WARTERAUM, Zustand " + f.Client.Zustand);
            await admin.PflichtAsync("freigeben", f.Identitaet.Fingerabdruck.Anzeige).ConfigureAwait(false);
            if (!await f.Client.WartenAufAsync(z => z == ClientZustand.AktivUdp, TimeSpan.FromSeconds(20)).ConfigureAwait(false))
                throw new InvalidOperationException(f.Benutzer + ": nicht AKTIV/UDP, Zustand " + f.Client.Zustand);
        }
        finally
        {
            parallel.Release();
        }
    }

    /// <summary>Felder nach dem Szenario: fremde Netze leeren, fehlende setzen (FeldSetzen, offene Netze).</summary>
    private async Task FelderSetzenAsync(Szenario s)
    {
        await Task.WhenAll(_funker.Select(async f =>
        {
            var soll = s.Felder[f.Index];
            var ist = f.Client.Lage.Mitgliedschaften?.Felder ?? [];
            foreach (var m in ist.Where(m => !soll.Contains(m.NetzId)))
                Pruefen(await f.Client.FeldSetzenAsync(m.Feld, null).ConfigureAwait(false), f, "leeren");
            var belegt = ist.Where(m => soll.Contains(m.NetzId)).Select(m => m.Feld).ToHashSet();
            foreach (var netz in soll.Where(n => !ist.Any(m => m.NetzId == n)))
            {
                byte feld = 0;
                while (belegt.Contains(feld)) feld++;
                belegt.Add(feld);
                Pruefen(await f.Client.FeldSetzenAsync(feld, netz).ConfigureAwait(false), f, "setzen");
            }
        })).ConfigureAwait(false);
        var bis = Stopwatch.StartNew();
        while (!_funker.All(f => Stimmt(f, s)))
        {
            if (bis.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Mitgliedschaften nicht angekommen");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static bool Stimmt(Funker f, Szenario s)
    {
        var ist = f.Client.Lage.Mitgliedschaften?.Felder ?? [];
        var soll = s.Felder[f.Index];
        return ist.Count == soll.Count && soll.All(n => ist.Any(m => m.NetzId == n && m.Hoeren && m.Senden));
    }

    private static void Pruefen(Antwort a, Funker f, string was)
    {
        if (!a.Ok) throw new InvalidOperationException(f.Benutzer + ": FeldSetzen " + was + " gescheitert: " + a.Code);
    }

    private async Task<Szenarioergebnis> SzenarioAsync(Szenario s, byte lauf, double? sekunden = null)
    {
        var takte = (int)Math.Round((sekunden ?? optionen.Sekunden) * 50);
        if (sekunden is null) aus.WriteLine($"Szenario {s.Kennung}: {s.Beschreibung}, {s.Sprecher.Count} Sprecher, {s.Stroeme} Ströme, {optionen.Sekunden} s");
        await FelderSetzenAsync(s).ConfigureAwait(false);
        foreach (var f in _funker)
        {
            f.Statistik.Zuruecksetzen(lauf);
            f.Gesendet = 0;
        }
        await Task.Delay(300).ConfigureAwait(false);

        var vorher = await RelaisAsync().ConfigureAwait(false);
        var proben = new List<(double Cpu, long Ram, long Privat)>();
        var relais = new List<(double P50, double P99, int Stufe)>();
        using var messenStop = new CancellationTokenSource();
        var messen = Task.Run(() => MessenAsync(proben, relais, messenStop.Token));

        var sprecher = s.Sprecher.Select(x => (Funker: _funker[x.Key], Ziel: x.Value, Puffer: new byte[optionen.OpusBytes])).ToList();
        foreach (var (f, ziel, _) in sprecher)
            if (!f.Client.Sprechen.Beginnen(ziel)) throw new InvalidOperationException(f.Benutzer + ": Beginnen gescheitert");
        using var wechselStop = new CancellationTokenSource();
        var wechsel = s.ChWechselNetz is { } wn && sekunden is null ? Task.Run(() => ChWechselnAsync(wn, wechselStop.Token)) : null;
        // Gruppen = 1: alle Sprecher im selben Takt (schlimmster Fall, ein Stoß je 20 ms). Gruppen = G: G Gruppen,
        // gleichmäßig über die 20 ms versetzt, wie unabhängige Audio-Uhren; jeder Sprecher sendet weiter alle 20 ms.
        var gruppen = Math.Clamp(optionen.Gruppen, 1, 20);
        var takt = new Taktgeber(TimeSpan.FromMilliseconds(20.0 / gruppen));
        var uhr = Stopwatch.StartNew();
        await takt.LaufenAsync(takte * gruppen, (j, _) =>
        {
            var gruppe = j % gruppen;
            var nummer = (uint)(j / gruppen);
            foreach (var (f, _, puffer) in sprecher)
            {
                if (f.Index % gruppen != gruppe) continue;
                Testrahmen.Schreiben(puffer, lauf, (ushort)f.Index, nummer, Stopwatch.GetTimestamp());
                if (f.Client.Sprechen.Senden(puffer)) f.Gesendet++;
            }
        }, CancellationToken.None).ConfigureAwait(false);
        var dauer = uhr.Elapsed.TotalSeconds;
        await wechselStop.CancelAsync().ConfigureAwait(false);
        if (wechsel is not null)
        {
            var (ms, fehler) = await wechsel.ConfigureAwait(false);
            aus.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Ch.-Wechsel: {ms.Count} in {dauer:F1} s ({ms.Count / dauer:F1}/s), gescheitert {fehler}, Antwort p50 {Quantil(ms, 0.5):F2} ms, p99 {Quantil(ms, 0.99):F2} ms, max {(ms.Count == 0 ? 0 : ms.Max()):F2} ms"));
        }
        foreach (var (f, _, _) in sprecher) f.Client.Sprechen.Beenden();
        await Task.Delay(1000).ConfigureAwait(false);
        await messenStop.CancelAsync().ConfigureAwait(false);
        await messen.ConfigureAwait(false);
        var nachher = await RelaisAsync().ConfigureAwait(false);
        return Auswerten(s, dauer, sprecher.Select(x => x.Funker).ToList(), takt, proben, relais, vorher, nachher);
    }

    private async Task MessenAsync(List<(double, long, long)> proben, List<(double, double, int)> relais, CancellationToken stop)
    {
        var letzteCpu = server is null ? TimeSpan.Zero : Cpu();
        var letzte = Stopwatch.GetTimestamp();
        var n = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(1000, stop).ConfigureAwait(false);
                if (server is not null)
                {
                    server.Refresh();
                    var cpu = server.TotalProcessorTime;
                    var jetzt = Stopwatch.GetTimestamp();
                    var wand = (jetzt - letzte) / (double)Stopwatch.Frequency;
                    proben.Add(((cpu - letzteCpu).TotalSeconds / wand * 100, server.WorkingSet64, server.PrivateMemorySize64));
                    letzteCpu = cpu;
                    letzte = jetzt;
                }
                if (++n % 2 == 0 && await RelaisAsync().ConfigureAwait(false) is { } m)
                    relais.Add((Zahl(m, "verarbeitungMsP50"), Zahl(m, "verarbeitungMsP99"), (int)Zahl(m, "laststufe")));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private TimeSpan Cpu()
    {
        server!.Refresh();
        return server.TotalProcessorTime;
    }

    private async Task<JsonNode?> RelaisAsync()
    {
        try
        {
            return await admin.MesswerteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private static double Zahl(JsonNode? n, string name)
    {
        if (n is not JsonObject o) return 0;
        foreach (var (k, v) in o)
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase) && v is JsonValue w && w.TryGetValue<double>(out var d)) return d;
        return 0;
    }

    private Szenarioergebnis Auswerten(Szenario s, double dauer, List<Funker> sprecher, Taktgeber takt,
        List<(double Cpu, long Ram, long Privat)> proben, List<(double P50, double P99, int Stufe)> relais, JsonNode? vorher, JsonNode? nachher)
    {
        var jeEmpfaenger = new List<Empfaengerwert>();
        var alleLaufzeiten = new List<float>();
        var jitter = new List<double>();
        long erwartetGesamt = 0, empfangenGesamt = 0, bytesGesamt = 0, paketeGesamt = 0, tunnel = 0;
        foreach (var r in _funker)
        {
            long erwartet = 0, empfangen = 0;
            foreach (var sp in sprecher)
            {
                if (!s.Hoerer[sp.Index].Contains(r.Index)) continue;
                erwartet += sp.Gesendet;
                empfangen += r.Statistik.Anzahl(sp.Index);
                jitter.Add(r.Statistik.Jitter(sp.Index));
            }
            var lz = r.Statistik.Laufzeiten();
            Array.Sort(lz);
            alleLaufzeiten.AddRange(lz);
            erwartetGesamt += erwartet;
            empfangenGesamt += empfangen;
            bytesGesamt += r.Statistik.Bytes;
            paketeGesamt += r.Statistik.Pakete;
            tunnel += r.Statistik.Tunnel;
            if (erwartet == 0 && r.Statistik.Pakete == 0) continue;
            var jr = sprecher.Where(sp => s.Hoerer[sp.Index].Contains(r.Index)).Select(sp => r.Statistik.Jitter(sp.Index)).DefaultIfEmpty(0).Max();
            jeEmpfaenger.Add(new Empfaengerwert(r.Index, erwartet, empfangen, erwartet == 0 ? 0 : 100.0 * (erwartet - empfangen) / erwartet,
                Szenarioergebnis.Quantil(lz, 0.5), Szenarioergebnis.Quantil(lz, 0.99), jr, r.Statistik.Pakete / dauer, r.Statistik.Bytes * 8 / dauer / 1000, r.Statistik.Tunnel));
        }
        var sortiert = alleLaufzeiten.ToArray();
        Array.Sort(sortiert);
        var gesendet = sprecher.Sum(sp => sp.Gesendet);
        // hoch: Kopf 16 + 9 + 2n + Opus + Tag 16 (5.4); Leitung: +28 IPv4/UDP, +26 PPPoE/Ethernet (7.4).
        var hochBytes = sprecher.Sum(sp => sp.Gesendet * (41L + 2 * s.Sprecher[sp.Index].Netze.Length + optionen.OpusBytes + 54));
        return new Szenarioergebnis(
            s.Kennung, s.Beschreibung, optionen.Funker, sprecher.Count, s.Stroeme, dauer, optionen.OpusBytes, Math.Clamp(optionen.Gruppen, 1, 20), gesendet, erwartetGesamt, empfangenGesamt,
            erwartetGesamt == 0 ? 0 : 100.0 * (erwartetGesamt - empfangenGesamt) / erwartetGesamt,
            jeEmpfaenger.Count == 0 ? 0 : jeEmpfaenger.Max(e => e.VerlustProzent),
            Szenarioergebnis.Quantil(sortiert, 0.5), Szenarioergebnis.Quantil(sortiert, 0.95), Szenarioergebnis.Quantil(sortiert, 0.99),
            sortiert.Length == 0 ? 0 : sortiert[^1],
            jitter.Count == 0 ? 0 : jitter.Average(), jitter.Count == 0 ? 0 : jitter.Max(),
            jeEmpfaenger.Count == 0 ? 0 : jeEmpfaenger.Average(e => e.PaketeJeS), jeEmpfaenger.Count == 0 ? 0 : jeEmpfaenger.Max(e => e.PaketeJeS),
            jeEmpfaenger.Count == 0 ? 0 : jeEmpfaenger.Average(e => e.KbitJeS), jeEmpfaenger.Count == 0 ? 0 : jeEmpfaenger.Max(e => e.KbitJeS),
            paketeGesamt / dauer, bytesGesamt * 8 / dauer / 1e6, (bytesGesamt + paketeGesamt * 54) * 8 / dauer / 1e6, hochBytes * 8 / dauer / 1e6,
            proben.Count == 0 ? 0 : proben.Average(p => p.Cpu), proben.Count == 0 ? 0 : proben.Max(p => p.Cpu),
            proben.Count == 0 ? 0 : proben.Max(p => p.Ram) / 1048576.0, proben.Count == 0 ? 0 : proben.Max(p => p.Privat) / 1048576.0,
            relais.Count == 0 ? 0 : relais.Average(r => r.P50), relais.Count == 0 ? 0 : relais.Max(r => r.P99),
            (long)(Zahl(nachher, "verworfenPruefung") + Zahl(nachher, "verworfenVoll") - Zahl(vorher, "verworfenPruefung") - Zahl(vorher, "verworfenVoll")),
            (long)(Zahl(nachher, "ausgangAltersverwurf") - Zahl(vorher, "ausgangAltersverwurf")),
            relais.Count == 0 ? 0 : relais.Max(r => r.Stufe),
            Quantil(takt.VerspaetungMs, 0.99),
            tunnel,
            jeEmpfaenger);
    }

    private static double Quantil(IReadOnlyList<double> werte, double q)
    {
        if (werte.Count == 0) return 0;
        var s = werte.Order().ToArray();
        return s[Math.Clamp((int)Math.Ceiling(q * s.Length) - 1, 0, s.Length - 1)];
    }
}

/// <summary>Aufbau je Funker: TLS mit Pin (Betrieb) oder Testzugang (Loopback), jeweils mit eigener lokaler Adresse.</summary>
internal static class Aufbauten
{
    public static Func<int, Geraeteidentitaet, IVerbindungsaufbau> Test(IPEndPoint zugang) =>
        (i, id) => new TestzugangAufbau(zugang, id, Funker.Adresse(i));

    public static Func<int, Geraeteidentitaet, IVerbindungsaufbau> Tls(Protokoll.Identitaet.Serverprofil profil, string? host, ushort? port, bool eigeneAdressen) =>
        (i, id) => new TlsAufbau(profil, id, new TlsAufbauOptionen { Host = host, Port = port, LokaleAdresse = eigeneAdressen ? Funker.Adresse(i) : null });
}
