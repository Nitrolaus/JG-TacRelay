// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen der Gegenprüfung Klangstufe A, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie zeigen, wo die Halteregel der
/// <see cref="Pegelstufe"/> außerhalb des Klangkorpus doch regelt: wenn der Nutzer schweigt und andere im Raum oder über die
/// Lautsprecher sprechen, beim Start mit einem untypischen ersten Satz, bei sehr lautem und sehr leisem Mikrofon und mit der
/// Sprechtaste über mehrere Sätze. Gezählt werden die Schritte der Verstärkung, wie viele davon in eine laufende Sendung fallen
/// (hörbar), der Pegel der Sprache am Gate und der Begrenzer.
/// </summary>
internal static class MessungGegenpruefungKlangA
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Gegenprüfung Klangstufe A übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    internal sealed record Teil(string Name, float[] Signal, bool Nutzer, bool Taste = false);

    internal sealed record Abschnitt(string Name, int Von, int Bis, bool Nutzer);

    internal sealed record Lauf(List<TelemetrieEintrag> T, List<Abschnitt> Abschnitte);

    /// <summary>Korpus von Äußerung <paramref name="von"/> bis vor Äußerung <paramref name="bis"/>, mit den Pausen dazwischen.</summary>
    internal static float[] Stueck(float[] eingang, int von, int bis)
    {
        var a = Klangkorpus.Aeusserungen;
        var start = Math.Max(0, a[von].Start - 14400) / 480 * 480;
        var ende = (bis < a.Count ? a[bis].Start - 14400 : eingang.Length) / 480 * 480;
        return eingang[start..ende];
    }

    internal static float[] Wiederholen(float[] x, int mal)
    {
        var y = new float[x.Length * mal];
        for (var i = 0; i < mal; i++) x.CopyTo(y, i * x.Length);
        return y;
    }

    internal static float[] Begrenzt(float[] x)
    {
        var y = (float[])x.Clone();
        for (var i = 0; i < y.Length; i++) y[i] = Math.Clamp(y[i], -1f, 1f);
        return y;
    }

    internal static async Task<Lauf> Ablauf(LautstaerkeEinstellung l, IEnumerable<Teil> teile, bool sprachaktivierung = true,
        Action<Pruefstand>? einrichten = null, Action<Pruefstand, long>? jeTakt = null)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Lautstaerke = l;
        s.Maschine.Sprachaktivierung = sprachaktivierung;
        einrichten?.Invoke(s);
        await s.StartenAsync();
        var abschnitte = new List<Abschnitt>();
        foreach (var t in teile)
        {
            var von = s.Telemetrie.Count;
            if (t.Taste) s.Maschine.SprechtasteGedrueckt();
            s.Abspielen(t.Signal, jeTakt is null ? null : takt => jeTakt(s, takt));
            if (t.Taste) s.Maschine.SprechtasteLosgelassen();
            abschnitte.Add(new Abschnitt(t.Name, von, s.Telemetrie.Count, t.Nutzer));
        }
        return new Lauf([.. s.Telemetrie], abschnitte);
    }

    /// <summary>Je Abschnitt eine Zeile: Verstärkung am Anfang und Ende, Schritte (davon in einer Sendung), Sprache am Gate, Begrenzer.</summary>
    internal static List<string> Auswerten(Lauf l)
    {
        var t = l.T;
        var zeilen = new List<string>();
        foreach (var a in l.Abschnitte)
        {
            int schritte = 0, inSendung = 0, gesendet = 0, begrenzer = 0;
            float groesster = 0, groessterInSendung = 0, tiefster = 0;
            var liste = new List<string>();
            for (var f = Math.Max(1, a.Von); f < a.Bis; f++)
            {
                var d = t[f].PegelstufeDb - t[f - 1].PegelstufeDb;
                if (t[f].Sendet) gesendet++;
                if (t[f].BegrenzerDb < -0.1f) begrenzer++;
                tiefster = MathF.Min(tiefster, t[f].BegrenzerDb);
                if (MathF.Abs(d) <= 0.01f) continue;
                schritte++;
                groesster = MathF.Max(groesster, MathF.Abs(d));
                if (t[f].Sendet || t[f - 1].Sendet)
                {
                    inSendung++;
                    groessterInSendung = MathF.Max(groessterInSendung, MathF.Abs(d));
                }
                if (liste.Count < 12) liste.Add(string.Create(C, $"{(f - a.Von) / 100.0:0.0}s{d:+0.0;-0.0}{(t[f].Sendet || t[f - 1].Sendet ? "*" : "")}"));
            }
            var sprache = "";
            if (a.Nutzer && a.Bis > a.Von)
            {
                var spitze = Enumerable.Range(a.Von, a.Bis - a.Von).Max(f => t[f].GatePegelDbfs);
                var aktiv = Enumerable.Range(a.Von, a.Bis - a.Von).Where(f => t[f].GatePegelDbfs > spitze - 25f).ToList();
                var mittel = 10 * Math.Log10(aktiv.Average(f => Math.Pow(10, t[f].GatePegelDbfs / 10.0)));
                sprache = string.Create(C, $", Sprache am Gate {mittel:0.0} dBFS");
            }
            zeilen.Add(string.Create(C,
                $"{a.Name}: Verstärkung {t[a.Von].PegelstufeDb:+0.0;-0.0} -> {t[a.Bis - 1].PegelstufeDb:+0.0;-0.0} dB, Schritte {schritte} (größter {groesster:0.0}), in Sendung {inSendung} (größter {groessterInSendung:0.0}){sprache}, gesendet {gesendet} von {a.Bis - a.Von} Rahmen, Begrenzer {begrenzer} Rahmen (tiefste {tiefster:0.0} dB) [{string.Join(" ", liste)}]"));
        }
        return zeilen;
    }

    private static void Ausgeben(string kopf, Lauf l)
    {
        foreach (var z in Auswerten(l)) Console.WriteLine($"  MESSUNG GP {kopf} | {z}");
    }

    /// <summary>
    /// Der Nutzer schweigt 220 s, andere sprechen im Raum (Korpus 12, 20 oder 30 dB leiser, Headset ohne Echo); davor und danach
    /// spricht er selbst. Vergleich: dieselbe Zeit nur Rauschen. Mit Sprechtaste ändert das nichts, die Pegelstufe läuft immer.
    /// </summary>
    [Test]
    public static async Task Messung_GP_Hintergrundsprache()
    {
        if (Aus()) return;
        var e0 = Pumpmessung.Eingang(0f);
        var nutzer1 = Stueck(e0, 0, 8);
        var nutzer2 = Stueck(e0, 8, 22);
        var rauschen = AnbindungSignale.Rauschen(220 * 48000, Pumpmessung.GrundrauschenDbfs, 99);
        var faelle = new List<(string Name, float[] Mitte)> { ("nur Rauschen", rauschen) };
        foreach (var db in new[] { -12f, -20f, -30f })
        {
            var h = Pumpmessung.Eingang(db, seed: 99);
            faelle.Add((string.Create(C, $"andere {db:0} dB"), Wiederholen(h, 2)[..rauschen.Length]));
        }
        var laeufe = faelle.Select(f => Task.Run(async () => (f.Name, Lauf: await Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
            [new Teil("Nutzer 1", nutzer1, true), new Teil("schweigt, " + f.Name, f.Mitte, false), new Teil("Nutzer 2", nutzer2, true)])))).ToList();
        foreach (var r in await Task.WhenAll(laeufe)) Ausgeben("Hintergrund " + r.Name, r.Lauf);
    }

    /// <summary>
    /// Start mit gelerntem Wert (wie nach einer früheren Sitzung), aber der erste erkannte Satz ist untypisch: ein lauter Ruf
    /// (Äußerung 2 um 10 dB lauter) oder ein anderer im Raum (20 dB leiser). Danach normale Sätze.
    /// </summary>
    [Test]
    public static async Task Messung_GP_Warmstart()
    {
        if (Aus()) return;
        var e0 = Pumpmessung.Eingang(0f);
        var warm = (await Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), [new Teil("lernen", e0, true)])).T[^1].PegelstufeDb;
        var nutzer2 = Stueck(e0, 8, 22);
        var laut = Begrenzt(Stueck(Pumpmessung.Eingang(10f), 2, 3));
        var anderer = Stueck(Pumpmessung.Eingang(-20f, seed: 5), 0, 1);
        var faelle = new (string Name, Teil[] Teile)[]
        {
            ("normal", [new Teil("Nutzer 2", nutzer2, true)]),
            ("erst lauter Ruf", [new Teil("Ruf +10 dB", laut, true), new Teil("Nutzer 2", nutzer2, true)]),
            ("erst anderer -20 dB", [new Teil("anderer -20 dB", anderer, false), new Teil("Nutzer 2", nutzer2, true)]),
        };
        Console.WriteLine(string.Create(C, $"  MESSUNG GP Warmstart: gelernter Wert {warm:+0.0;-0.0} dB"));
        var laeufe = faelle.Select(f => Task.Run(async () => (f.Name, Lauf: await Ablauf(new LautstaerkeEinstellung(true, warm, Gelernt: true), f.Teile)))).ToList();
        foreach (var r in await Task.WhenAll(laeufe)) Ausgeben("Warmstart " + r.Name, r.Lauf);
    }

    /// <summary>
    /// Mikrofonpegel außerhalb des Korpusbereichs: sehr laut (+6 und +10 dB, am Wandler auf [-1, 1] begrenzt) und sehr leise
    /// (-30, -35 dB), kalt und warm, dazu die alte Kette. Kennzahlen wie <see cref="MessungKlangA.Messung_Pumpen"/>.
    /// </summary>
    [Test]
    public static async Task Messung_GP_Mikrofonpegel()
    {
        if (Aus()) return;
        var zeilen = new List<(float Mikro, int Nr, string Zeile)>();
        var laeufe = new List<Task>();
        foreach (var mikro in new[] { 10f, 6f, -30f, -35f })
        {
            var eingang = Begrenzt(Pumpmessung.Eingang(mikro));
            var warm = (await Pumpmessung.Ausfuehren(Variante.Halteregel(), mikro, eingang)).Telemetrie[^1].PegelstufeDb;
            var varianten = new[] { Variante.Alt, Variante.Halteregel(), Variante.Halteregel(warm, gelernt: true) };
            for (var i = 0; i < varianten.Length; i++)
            {
                var (v, nr) = (varianten[i], i);
                laeufe.Add(Task.Run(async () =>
                {
                    var l = await Pumpmessung.Ausfuehren(v, mikro, eingang);
                    var k = Pumpmessung.Auswerten(l);
                    var schritte = new List<string>();
                    for (var f = 1; f < l.Telemetrie.Count && schritte.Count < 14; f++)
                    {
                        var d = l.Telemetrie[f].PegelstufeDb - l.Telemetrie[f - 1].PegelstufeDb;
                        if (MathF.Abs(d) > 0.01f) schritte.Add(string.Create(C, $"{f / 100.0:0.0}s{d:+0.0;-0.0}"));
                    }
                    var spitze = l.Telemetrie.Max(e => e.VerstaerktDbfs);
                    lock (zeilen) zeilen.Add((mikro, nr, Pumpmessung.Zeile(l, k).Split(" | Laufzeit")[0] + string.Create(C, $" | lautester Rahmen vor RNNoise {spitze:0.0} dBFS | Schritte {string.Join(" ", schritte)}")));
                }));
            }
        }
        await Task.WhenAll(laeufe);
        foreach (var z in zeilen.OrderByDescending(z => z.Mikro).ThenBy(z => z.Nr)) Console.WriteLine("  MESSUNG GP Pegel " + z.Zeile);
    }

    /// <summary>
    /// Sprechtaste über mehrere Sätze gehalten (natürliche Pausen 150 ms bis 3 s), kalt: Schritte, die in die laufende Sendung
    /// fallen, sind für die anderen hörbar. Dazu dieselben Sätze mit Sprachaktivierung.
    /// </summary>
    [Test]
    public static async Task Messung_GP_Sprechtaste()
    {
        if (Aus()) return;
        var e0 = Pumpmessung.Eingang(0f);
        var e15 = Pumpmessung.Eingang(-15f);
        var gruppen = new[] { (0, 4), (4, 8), (8, 12), (12, 15), (15, 22) };
        foreach (var (name, e) in new[] { ("Mikro 0 dB", e0), ("Mikro -15 dB", e15) })
        {
            // Zwischen zwei Sendungen 2 s ohne Taste (die anderen antworten), wie im Funkverkehr.
            var luecke = AnbindungSignale.Rauschen(2 * 48000, Pumpmessung.GrundrauschenDbfs, 5);
            var taste = gruppen.SelectMany(g => new[] { new Teil($"Taste Äußerung {g.Item1}-{g.Item2 - 1}", Stueck(e, g.Item1, g.Item2), true, Taste: true), new Teil("Lücke", luecke, false) }).ToArray();
            var va = gruppen.Select(g => new Teil($"VA Äußerung {g.Item1}-{g.Item2 - 1}", Stueck(e, g.Item1, g.Item2), true)).ToArray();
            var kalt = new LautstaerkeEinstellung(true, Pegelstufe.StartDb);
            var lt = Ablauf(kalt, taste, sprachaktivierung: false);
            var lv = Ablauf(kalt, va);
            Ausgeben($"Taste kalt {name}", await lt);
            Ausgeben($"VA kalt {name}", await lv);
        }
    }

    /// <summary>
    /// Eigener Druck-Klick über Lautsprecher mit dem Zeitverhalten echter Geräte (<see cref="KlickLautsprecherTests.AblaufMitVerzug"/>):
    /// Verzug vom Mischen bis in den Loopback 0 bis 8 Rahmen, Rückstand im Ring 0 bis 7. Anlaut 50 und 100 ms nach dem Druck gegen
    /// denselben Ablauf ohne Klick (Mittel, schlechtester; 2 Saaten zu 5 Sprüchen), Klick aus der Referenz genommen und zum Vergleich in
    /// der Referenz.
    /// </summary>
    [Test]
    public static async Task Messung_GP_KlickVerzug()
    {
        if (Aus()) return;
        foreach (var (name, echo) in new (string, Func<float, float>?)[] { ("kein Echo", null), ("12 dB", KlangMaschineTests.Kopplung12Db) })
        {
            foreach (var (verzug, rueckstand) in new[] { (0, 0), (3, 1), (5, 4), (5, 7), (8, 7) })
            {
                foreach (var reaktion in new[] { 50, 100 })
                {
                    var teile = new List<string>();
                    foreach (var ausblenden in new[] { true, false })
                    {
                        var d = new List<double>();
                        foreach (var saat in new[] { 5, 10 })
                        {
                            var ohne = await KlickLautsprecherTests.AblaufMitVerzug(saat, false, echo, reaktion, verzug, rueckstand);
                            var mit = await KlickLautsprecherTests.AblaufMitVerzug(saat, true, echo, reaktion, verzug, rueckstand, ausblenden: ausblenden);
                            d.AddRange(mit.Zip(ohne, (m, o) => m - o));
                        }
                        teile.Add(string.Create(C, $"{(ausblenden ? "aus der Referenz" : "in der Referenz")} {d.Average():+0.0;-0.0;0.0}/{d.Min():+0.0;-0.0;0.0} dB"));
                    }
                    Console.WriteLine(string.Create(C, $"  MESSUNG GP Klick {name}, Verzug {verzug}, Rückstand {rueckstand}, Sprechen {reaktion} ms: {string.Join(" | ", teile)}"));
                }
            }
        }
    }

    /// <summary>
    /// Nebenwirkung des ausgeblendeten Klicks: Lautsprecher (12 dB, 40 ms), ein anderer Funker spricht bzw. Spielton läuft, der
    /// Nutzer drückt die Taste und schweigt 400 ms. Pegel des Gesendeten in den ersten 250 ms (dekodiert): Klick aus der Referenz
    /// (die Referenz ist 120 ms still, das Echo bleibt stehen), Klick in der Referenz und ohne eigenen Klick (Referenz vollständig).
    /// </summary>
    [Test]
    public static async Task Messung_GP_KlickNebenwirkung()
    {
        if (Aus()) return;
        foreach (var quelle in new[] { "anderer Funker", "Spielton -24 dBFS", "Spielton -36 dBFS" })
        {
            var teile = new List<string>();
            foreach (var (weg, klick, ausblenden) in new[] { ("aus der Referenz", true, true), ("in der Referenz", true, false), ("ohne Klick", false, true) })
            {
                var pegel = new List<double>();
                foreach (var saat in new[] { 5, 10, 15 })
                {
                    await using var s = new Pruefstand(klickStartwert: saat);
                    s.Maschine.Echounterdrueckung = true;
                    s.Maschine.Sprachaktivierung = false;
                    s.Maschine.Kette.EigenenKlickAusblenden = ausblenden;
                    s.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = klick, AndereAn = false };
                    await s.StartenAsync();
                    var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
                    s.Echo = (w, mic) =>
                    {
                        for (var i = 0; i < mic.Length; i++)
                        {
                            linie.Enqueue(KlangMaschineTests.Kopplung12Db(0.5f * (w[2 * i] + w[2 * i + 1])));
                            mic[i] += linie.Dequeue();
                        }
                    };
                    var takte = 1200;
                    var fern = AnbindungSignale.Sprache(takte * 480, (ulong)(saat + 500), -24);
                    var spiel = quelle.StartsWith("Spielton", StringComparison.Ordinal)
                        ? MessungSpielton.Spielton(takte * 480, quelle.Contains("-24", StringComparison.Ordinal) ? -24f : -36f, saat) : null;
                    if (spiel is not null)
                    {
                        s.Fremdton = (t, w) =>
                        {
                            var ab = (int)((t - 1) % takte) * 480;
                            for (var i = 0; i < 480; i++)
                            {
                                w[2 * i] += spiel[ab + i];
                                w[2 * i + 1] += spiel[ab + i];
                            }
                        };
                    }
                    using var kodierer = new OpusKodierer();
                    var puffer = new byte[OpusKodierer.MaxPaketBytes];
                    uint frame = 0;
                    var grund = AnbindungSignale.Rauschen(480, -65, (ulong)saat);
                    var phasenStart = s.Pakete.Count;
                    for (var t = 0; t < takte; t++)
                    {
                        if (spiel is null && t % 2 == 0 && t + 2 < takte)
                        {
                            var m = kodierer.Kodieren(fern.AsSpan(t * 480, 960), puffer);
                            _ = s.Maschine.SpracheEmpfangen(77, 0, 3, frame, frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer.AsSpan(0, m), s.Uhr.Us);
                            frame++;
                        }
                        if (t % 200 == 100) s.Maschine.SprechtasteGedrueckt();
                        if (t % 200 == 140) s.Maschine.SprechtasteLosgelassen();
                        s.Takt(grund);
                    }
                    List<byte[]>? phase = null;
                    foreach (var p in s.Pakete.Cast<Gesendet?>().Append(null))
                    {
                        if (p is null || (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
                        {
                            if (phase is { Count: > 0 })
                            {
                                using var dek = new OpusDekodierer();
                                var pcm = new List<float>();
                                var rahmen = new float[OpusDekodierer.MaxSamples];
                                foreach (var paket in phase) pcm.AddRange(rahmen.AsSpan(0, dek.Dekodieren(paket, rahmen)).ToArray());
                                if (pcm.Count >= 12000) pegel.Add(Pegel.Dbfs(pcm.ToArray().AsSpan(0, 12000)));
                            }
                            phase = [];
                        }
                        if (p is not null && p.Opus.Length > 0) phase?.Add(p.Opus);
                    }
                }
                var ohneErste = pegel.Count > 3 ? pegel.Skip(1).ToList() : pegel;
                teile.Add(string.Create(C, $"{weg} Mittel {10 * Math.Log10(ohneErste.Average(x => Math.Pow(10, x / 10))):0.0} höchstens {ohneErste.Max():0.0} dBFS ({pegel.Count} Sendungen)"));
            }
            Console.WriteLine($"  MESSUNG GP Klick-Nebenwirkung {quelle}, gesendet in den ersten 250 ms: " + string.Join(" | ", teile));
        }
    }

    /// <summary>
    /// Laufzeit der Sendekette je 10-ms-Rahmen (Telemetrie der Kette), Korpus 110 s, alte und neue Kette: ohne Last und unter
    /// künstlicher Last (so viele Threads wie Kerne rechnen ohne Pause mit Normalpriorität); die Kette läuft in einem eigenen Thread
    /// mit Normal- oder höchster Priorität (MMCSS gibt es im Prüfstand nicht; im Programm läuft der Aufnahme-Thread mit MMCSS).
    /// </summary>
    [Test]
    public static void Messung_GP_Echtzeit()
    {
        if (Aus()) return;
        var eingang = Pumpmessung.Eingang(0f);
        var faelle = new (string Name, int Last, ThreadPriority Prio)[]
        {
            ("ohne Last", 0, ThreadPriority.Normal), ("Last, Kette normal", Environment.ProcessorCount, ThreadPriority.Normal),
            ("Last, Kette höchste", Environment.ProcessorCount, ThreadPriority.Highest),
        };
        foreach (var (name, last, prio) in faelle)
        {
            foreach (var v in new[] { Variante.Alt, Variante.Halteregel() })
            {
                using var stopp = new CancellationTokenSource();
                var lastThreads = Enumerable.Range(0, last).Select(_ =>
                {
                    var t = new Thread(() =>
                    {
                        var x = 1.0;
                        while (!stopp.IsCancellationRequested) x = Math.Sin(x) + 1.0;
                    }) { IsBackground = true };
                    t.Start();
                    return t;
                }).ToList();
                TelemetrieStand? stand = null;
                var kette = new Thread(() =>
                {
                    var s = new Pruefstand(mitPegelstufe: v.MitPegelstufe, apm: v.Apm);
                    try
                    {
                        if (v.MitPegelstufe) s.Maschine.Lautstaerke = v.Lautstaerke;
                        s.StartenAsync().GetAwaiter().GetResult();
                        s.Abspielen(eingang);
                        stand = s.Maschine.Telemetrie;
                    }
                    finally
                    {
                        s.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }) { Priority = prio };
                kette.Start();
                kette.Join();
                stopp.Cancel();
                foreach (var t in lastThreads) t.Join();
                string Lz(KlangStufe k) => string.Create(C, $"{stand!.Stufe(k).P50Us:0}/{stand.Stufe(k).P95Us:0}/{stand.Stufe(k).P99Us:0}/{stand.Stufe(k).MaxUs:0}");
                Console.WriteLine(string.Create(C, $"  MESSUNG GP Echtzeit {name} ({last} Lastthreads), {v.Name}: p50/p95/p99/max µs Gesamt {Lz(KlangStufe.Gesamt)} | Echo {Lz(KlangStufe.Echo)} | RNNoise {Lz(KlangStufe.Rauschfilter)} | Pegel {Lz(KlangStufe.Pegelstufe)} | Begr {Lz(KlangStufe.Begrenzer)} | Gate {Lz(KlangStufe.Gate)} | Opus {Lz(KlangStufe.Opus)} | Fristen verpasst {stand!.DeadlineVerpasst} von {stand.Rahmen}"));
            }
        }
    }

    /// <summary>
    /// Der Nutzer schweigt 120 s mit Lautsprechern (Echounterdrückung an, Kopplung 12 dB bzw. 20 dB, 40 ms), ein anderer Funker
    /// spricht den Korpus; davor und danach spricht der Nutzer selbst. Lernt die Halteregel das Restecho als Stimme?
    /// </summary>
    [Test]
    public static async Task Messung_GP_FunkUeberLautsprecher()
    {
        if (Aus()) return;
        var e0 = Pumpmessung.Eingang(0f);
        var nutzer1 = Stueck(e0, 0, 8);
        var nutzer2 = Stueck(e0, 8, 22);
        var fern = Pumpmessung.Eingang(0f, seed: 3);
        var pakete = new List<(int Takt, ushort Phase, uint Frame, Sprachkennzeichen K, byte[] Daten)>();
        using (var kodierer = new OpusKodierer())
        {
            var puffer = new byte[OpusKodierer.MaxPaketBytes];
            var phase = (ushort)10;
            foreach (var a in Klangkorpus.Aeusserungen)
            {
                var von = Math.Max(0, a.Start - 4800) / 960 * 960;
                var bis = Math.Min(fern.Length, a.Ende + 4800) / 960 * 960;
                uint frame = 0;
                for (var i = von; i + 960 <= bis; i += 960, frame++)
                {
                    var m = kodierer.Kodieren(fern.AsSpan(i, 960), puffer);
                    pakete.Add((i / 480, phase, frame, frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer[..m]));
                }
                for (var k = 0; k < 3; k++) pakete.Add((bis / 480, phase, frame + (uint)k, Sprachkennzeichen.Ende, []));
                phase++;
            }
        }
        var stille = AnbindungSignale.Rauschen(fern.Length, Pumpmessung.GrundrauschenDbfs, 98);
        var faelle = new (string Name, Func<float, float>? Pfad, bool Funk)[]
        {
            ("12 dB, nur Rauschen", KlangMaschineTests.Kopplung12Db, false), ("12 dB, Funk", KlangMaschineTests.Kopplung12Db, true),
            ("20 dB, Funk", x => 0.1f * x, true), ("verzerrt, Funk", KlangMaschineTests.KopplungVerzerrt, true),
        };
        var laeufe = faelle.Select(f => Task.Run(async () =>
        {
            var start = -1;
            var naechstes = 0;
            var lauf = await Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
                [new Teil("Nutzer 1", nutzer1, true), new Teil("schweigt, " + f.Name, stille, false), new Teil("Nutzer 2", nutzer2, true)],
                einrichten: s =>
                {
                    s.Maschine.Echounterdrueckung = true;
                    var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
                    s.Echo = (w, mic) =>
                    {
                        for (var i = 0; i < mic.Length; i++)
                        {
                            linie.Enqueue(f.Pfad!(0.5f * (w[2 * i] + w[2 * i + 1])));
                            mic[i] += linie.Dequeue();
                        }
                    };
                },
                jeTakt: (s, takt) =>
                {
                    if (!f.Funk) return;
                    var abschnitt = s.Telemetrie.Count;
                    if (abschnitt < nutzer1.Length / 480 || abschnitt >= (nutzer1.Length + stille.Length) / 480) return;
                    if (start < 0) start = abschnitt;
                    while (naechstes < pakete.Count && pakete[naechstes].Takt <= abschnitt - start)
                    {
                        var p = pakete[naechstes++];
                        _ = s.Maschine.SpracheEmpfangen(77, 0, p.Phase, p.Frame, p.K, p.Daten, s.Uhr.Us);
                    }
                });
            return (f.Name, Lauf: lauf);
        })).ToList();
        foreach (var r in await Task.WhenAll(laeufe)) Ausgeben("Lautsprecher " + r.Name, r.Lauf);
    }
}
