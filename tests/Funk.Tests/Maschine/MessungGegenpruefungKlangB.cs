// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Geraete;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen der Gegenprüfung Klangstufe B, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie zeigen Verhalten, das die Messungen
/// des Baus nicht abdecken: leise Mikrofone und harte Mikrofon-Gates (libDF rechnet Rahmen unter etwa -70 dBFS nicht), Speicher
/// beim wiederholten Anlegen und Umschalten, Freigabezeit, Geräte mit 44,1 kHz.
/// </summary>
internal static class MessungGegenpruefungKlangB
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const int R = 480;

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Gegenprüfung Klangstufe B übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    private static double Energie(ReadOnlySpan<float> x)
    {
        double e = 0;
        foreach (var v in x) e += (double)v * v;
        return e;
    }

    private static double Db(double leistung) => 10 * Math.Log10(Math.Max(leistung, 1e-30));

    /// <summary>Korpus ohne Grundrauschen, jede Äußerung <paramref name="vorEndeMs"/> vor ihrem Ende hart abgeschnitten (Gate im Mikrofon-Treiber).</summary>
    internal static float[] HartesGate(int vorEndeMs)
    {
        var x = (float[])Klangkorpus.Signal.Clone();
        var a = Klangkorpus.Aeusserungen;
        for (var i = 0; i < a.Count; i++)
        {
            var von = Math.Max(a[i].Start, a[i].Ende - (vorEndeMs * 48));
            var bis = i + 1 < a.Count ? a[i + 1].Start : x.Length;
            x.AsSpan(von, bis - von).Clear();
        }
        // Vor der ersten Äußerung ebenfalls digitale Stille.
        x.AsSpan(0, a[0].Start).Clear();
        return x;
    }

    /// <summary>
    /// Wie lange vor jedem Satzanfang (nach dem Hochpass) etwas aus dem Filter kommt, obwohl der Eingang dort leise oder still ist:
    /// Fenster 60 bis 10 ms vor dem Anfang, zeitrichtig. libDF lässt Rahmen unter etwa -70 dBFS liegen (Zustand bleibt stehen); die
    /// zwei Rahmen vor der Stille kommen dann erst beim nächsten Anfang heraus.
    /// </summary>
    [Test]
    public static void Messung_GPB_LeisesMikrofonUndHartesGate()
    {
        if (Aus()) return;
        var sauber = MessungKlangB.Hochpass(Klangkorpus.Signal);
        var versatz = AnbindungSignale.BesterVerzug(Klangkorpus.Signal, sauber, Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 2400).Verzug;
        var szenen = new (string Name, Func<float[]> Eingang)[]
        {
            ("Grundrauschen -65 dBFS", () => Pumpmessung.Eingang(0f)),
            ("Grundrauschen -75 dBFS", () => Pumpmessung.Eingang(0f, rauschenDbfs: -75f)),
            ("Grundrauschen -85 dBFS", () => Pumpmessung.Eingang(0f, rauschenDbfs: -85f)),
            ("hartes Gate 0 ms vor Ende", () => HartesGate(0)),
            ("hartes Gate 150 ms vor Ende", () => HartesGate(150)),
            ("hartes Gate 400 ms vor Ende", () => HartesGate(400)),
        };
        foreach (var (name, eingang) in szenen)
        {
            var ein = MessungKlangB.Hochpass(eingang());
            var still = 0;
            for (var r = 0; r < ein.Length / R; r++) if (Energie(ein.AsSpan(r * R, R)) / R < 1e-7) still++;
            foreach (var f in new[] { MessungKlangB.Filter.Rnnoise, MessungKlangB.Filter.DeepFilterVoll })
            {
                var (aus, _) = MessungKlangB.Filtern(f, ein);
                var verzug = MessungKlangB.Verzug(f);
                var k = MessungKlangB.Bewerten(ein, aus, verzug);
                var geister = new List<double>();
                var einVor = new List<double>();
                var korrelationen = new List<double>();
                foreach (var a in Klangkorpus.Aeusserungen.Skip(1))
                {
                    var von = a.Start + versatz - 2880;
                    if (von + verzug + 2400 > aus.Length) continue;
                    geister.Add(Db(Energie(aus.AsSpan(von + verzug, 2400)) / 2400));
                    einVor.Add(Db(Energie(ein.AsSpan(von, 2400)) / 2400));
                }
                var sortiert = geister.Order().ToArray();
                Console.WriteLine(string.Create(C,
                    $"  MESSUNG GPB Geist {name}, {f}: Eingangsrahmen unter 1e-7 {still}/{ein.Length / R}; vor Anfängen (60-10 ms) Ausgang Median {sortiert[sortiert.Length / 2]:0.0} max {sortiert[^1]:0.0} dBFS, über -60 dBFS {geister.Count(g => g > -60)}/{geister.Count}, Eingang dort max {einVor.Max():0.0} dBFS | SI-SDR {k.SiSdrDb:0.0} dB, Sprache {k.SpracheDb:+0.0;-0.0} dB, Anfänge {k.AnfaengeGedaempft}/{k.Woerter}, Enden {k.EndenGedaempft}/{k.Saetze}, Rest {k.RestDbfs:0.0} dBFS"));
            }
        }
    }

    /// <summary>
    /// Dasselbe in der echten Kette mit gehaltener Sprechtaste (gesendet und von einer zweiten Maschine empfangen, wie die Hörproben):
    /// erster Block des Korpus (sieben Sätze), Vorlauf 1,5 s im selben Grundrauschen. Gemessen am Empfänger, zeitrichtig: Fenster 60 bis
    /// 10 ms vor jedem Satzanfang und die letzten 30 ms jedes Satzes gegen den Eingang.
    /// </summary>
    [Test]
    public static async Task Messung_GPB_GeistInDerKette()
    {
        if (Aus()) return;
        var ende = Klangkorpus.Aeusserungen[7].Start / R * R;
        var szenen = new (string Name, float[] Eingang, float[] Vorlauf)[]
        {
            ("Grundrauschen -65 dBFS", Pumpmessung.Eingang(0f)[..ende], AnbindungSignale.Rauschen(150 * R, -65, 78)),
            ("Grundrauschen -80 dBFS", Pumpmessung.Eingang(0f, rauschenDbfs: -80f)[..ende], AnbindungSignale.Rauschen(150 * R, -80, 78)),
            ("hartes Gate 150 ms vor Ende", HartesGate(150)[..ende], new float[150 * R]),
        };
        foreach (var (name, eingang, vorlauf) in szenen)
        {
            var teile = new List<string>();
            foreach (var f in new[] { Rauschfilter.Standard, Rauschfilter.Stark })
            {
                await using var sender = new Pruefstand();
                sender.Maschine.Rauschfilter = f;
                sender.Maschine.Sprachaktivierung = false;
                await sender.StartenAsync();
                sender.Abspielen(AnbindungSignale.Rauschen(150 * R, -50, 79));
                sender.Abspielen(vorlauf);
                var wirkt = sender.Maschine.Rauschfilterstand.Wirksam;
                sender.Maschine.SprechtasteGedrueckt();
                var start = (int)sender.Takte * R;
                sender.Abspielen(eingang);
                sender.Maschine.SprechtasteLosgelassen();
                sender.Stille(50);
                var mono = await MessungKlangA.Empfaenger(sender.Pakete, (int)sender.Takte + 80);
                var gesamt = new float[start + eingang.Length];
                eingang.CopyTo(gesamt, start);
                var verzug = AnbindungSignale.BesterVerzug(gesamt, mono, start + Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 12000).Verzug;
                var geister = new List<double>();
                var enden = new List<double>();
                foreach (var a in Klangkorpus.Aeusserungen.Take(7))
                {
                    if (a.Nr > Klangkorpus.Aeusserungen[0].Nr)
                        geister.Add(Db(Energie(mono.AsSpan(start + a.Start - 2880 + verzug, 2400)) / 2400));
                    var e1 = start + Math.Min(a.Ende, ende);
                    while (e1 > start + a.Start && Math.Abs(gesamt[e1 - 1]) < 1e-6f) e1--;
                    var e = e1 - 1440;
                    enden.Add(Db(Energie(mono.AsSpan(e + verzug, 1440)) / 1440) - Db(Energie(gesamt.AsSpan(e, 1440)) / 1440));
                }
                teile.Add(string.Create(C,
                    $"{f} (wirkt {wirkt}, Verzug {verzug}): vor Anfängen max {geister.Max():0.0} dBFS, über -60 {geister.Count(g => g > -60)}/{geister.Count}; letzte 30 ms gegen Eingang min {enden.Min():+0.0;-0.0} dB, Median {enden.Order().ElementAt(enden.Count / 2):+0.0;-0.0} dB"));
            }
            Console.WriteLine($"  MESSUNG GPB Geist Kette Taste {name}: " + string.Join(" | ", teile));
        }
    }

    /// <summary>Pegel je 10 ms um einen harten Schnitt (Sprache, dann Stille oder Rauschen -80 dBFS) und den nächsten Anfang, zeitrichtig.</summary>
    [Test]
    public static void Messung_GPB_SchnittVerlauf()
    {
        if (Aus()) return;
        const int Fs = AnbindungSignale.Fs;
        var sprache = AnbindungSignale.Sprache(Fs * 3, 12);
        int Lautester(int von, int bis)
        {
            int best = von;
            for (int f = von; f + R <= bis; f += R)
                if (AnbindungSignale.PegelDb(sprache.AsSpan(f, R)) > AnbindungSignale.PegelDb(sprache.AsSpan(best, R))) best = f;
            return best;
        }
        int schnitt = Lautester(Fs * 6 / 10, Fs * 12 / 10) + R;
        int weiter = Lautester(Fs * 3 / 2, Fs * 2);
        foreach (var boden in new[] { -300.0, -80.0, -60.0 })
        {
            var signal = boden > -200 ? AnbindungSignale.Rauschen(schnitt + Fs + Fs, boden, 3) : new float[schnitt + Fs + Fs];
            for (int i = 0; i < schnitt; i++) signal[i] += sprache[i];
            int anfang = schnitt + Fs;
            for (int i = 0; i < Fs; i++) signal[anfang + i] += sprache[weiter + i];
            foreach (var f in new[] { MessungKlangB.Filter.Rnnoise, MessungKlangB.Filter.DeepFilterVoll })
            {
                var (aus, _) = MessungKlangB.Filtern(f, signal);
                var v = MessungKlangB.Verzug(f);
                string Verlauf(int um) => string.Join(" ", Enumerable.Range(-5, 9).Select(k => string.Create(C,
                    $"{AnbindungSignale.PegelDb(aus.AsSpan(um + (k * R) + v, R)):0}/{AnbindungSignale.PegelDb(signal.AsSpan(um + (k * R), R)):0}")));
                Console.WriteLine($"  MESSUNG GPB Schnitt Boden {boden}, {f}: um den Schnitt (aus/ein je 10 ms, -50..+40 ms) {Verlauf(schnitt)} | um den Anfang {Verlauf(anfang)}");
            }
        }
    }

    /// <summary>Speicher und Zeiten beim wiederholten Anlegen, Rechnen und Freigeben von DeepFilterNet3.</summary>
    [Test]
    public static void Messung_GPB_AnlegenFreigeben()
    {
        if (Aus()) return;
        var signal = AnbindungSignale.Rauschen(100 * R, -40, 5);
        var aus = new float[R];
        var anlegen = new List<double>();
        var freigeben = new List<double>();
        long Privat()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var p = Process.GetCurrentProcess();
            return p.PrivateMemorySize64;
        }
        var start = Privat();
        var zwischen = new List<string>();
        for (var i = 1; i <= 60; i++)
        {
            var t = Stopwatch.GetTimestamp();
            var df = new DeepFilterProzessor(schwellen: DeepFilterSchwellen.Voll);
            anlegen.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            for (var r = 0; r < 100; r++) df.Verarbeiten(signal.AsSpan(r * R, R), aus);
            t = Stopwatch.GetTimestamp();
            df.Dispose();
            freigeben.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            if (i is 1 or 5 or 10 or 20 or 40 or 60) zwischen.Add(string.Create(C, $"{i}: {(Privat() - start) / 1048576.0:+0.0;-0.0} MB"));
        }
        string V(List<double> x) { var s = x.Order().ToArray(); return string.Create(C, $"p50 {s[s.Length / 2]:0.0} max {s[^1]:0.0} ms"); }
        Console.WriteLine($"  MESSUNG GPB Anlegen/Freigeben 60x: privat {string.Join(", ", zwischen)}; Anlegen {V(anlegen)}; Freigeben {V(freigeben)}");
    }

    /// <summary>Speicher beim wiederholten Umschalten Standard/Stark in der Maschine (jeweils bis Stark wirkt und wieder Standard).</summary>
    [Test]
    public static async Task Messung_GPB_Umschalten()
    {
        if (Aus()) return;
        await using var s = new Pruefstand();
        await s.StartenAsync();
        var rauschen = AnbindungSignale.Rauschen(80 * R, -50, 6);
        long Privat()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var p = Process.GetCurrentProcess();
            return p.PrivateMemorySize64;
        }
        s.Abspielen(rauschen);
        var start = Privat();
        var zwischen = new List<string>();
        var starkWirkte = 0;
        for (var i = 1; i <= 40; i++)
        {
            s.Maschine.Rauschfilter = Rauschfilter.Stark;
            s.Abspielen(rauschen);
            if (s.Maschine.Rauschfilterstand.Wirksam == Rauschfilter.Stark) starkWirkte++;
            s.Maschine.Rauschfilter = Rauschfilter.Standard;
            s.Abspielen(rauschen.AsSpan(0, 12 * R));
            if (i is 1 or 10 or 20 or 40) zwischen.Add(string.Create(C, $"{i}: {(Privat() - start) / 1048576.0:+0.0;-0.0} MB"));
        }
        Console.WriteLine($"  MESSUNG GPB Umschalten 40x: Stark wirkte {starkWirkte}/40, HatStark am Ende {s.Maschine.Kette.HatStark}, privat {string.Join(", ", zwischen)}");
    }

    /// <summary>Stark mit einem Mikrofon in 44,1 kHz stereo: wirkt es, wird gesendet, fällt es zurück?</summary>
    [Test]
    public static async Task Messung_GPB_44kHz()
    {
        if (Aus()) return;
        foreach (var (rate, kanaele) in new[] { (48000, 1), (44100, 2), (44100, 1) })
        {
            await using var s = new Pruefstand(aufnahme: new StromFormat(rate, kanaele));
            await s.StartenAsync();
            s.Maschine.Rauschfilter = Rauschfilter.Stark;
            var n = rate / 100;
            var sprache = AnbindungSignale.Sprache(n * 600, 7, -24);
            var rauschen = AnbindungSignale.Rauschen(n * 600, -50, 8);
            var takt = new float[n * kanaele];
            for (var t = 0; t < 600; t++)
            {
                for (var i = 0; i < n; i++)
                {
                    var v = (t < 100 ? 0f : sprache[t * n + i]) + rauschen[t * n + i];
                    for (var k = 0; k < kanaele; k++) takt[(kanaele * i) + k] = v;
                }
                if (t == 150) s.Maschine.SprechtasteGedrueckt();
                if (t == 550) s.Maschine.SprechtasteLosgelassen();
                s.Takt(takt);
            }
            var st = s.Maschine.Rauschfilterstand;
            var tel = s.Maschine.Telemetrie;
            var g = tel.Stufe(KlangStufe.Gesamt);
            var rf = tel.Stufe(KlangStufe.Rauschfilter);
            Console.WriteLine(string.Create(C, $"  MESSUNG GPB {rate} Hz x {kanaele}: gewählt {st.Gewaehlt}, wirkt {st.Wirksam}, Rückfall {st.Rueckfall ?? "keiner"}, Pakete {s.Pakete.Count}, Fehler {s.Fehler.Count}, Kette p50/p99/max {g.P50Us:0}/{g.P99Us:0}/{g.MaxUs:0} µs, Filter p99/max {rf.P99Us:0}/{rf.MaxUs:0} µs, Fristen {tel.DeadlineVerpasst}"));
        }
    }
}
