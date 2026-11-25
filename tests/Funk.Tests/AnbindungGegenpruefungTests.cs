// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>
/// Gegenprüfung der Klang-Anbindung: numerische Randfälle (NaN, Unendlich, riesige und subnormale
/// Werte), Zufallspakete an der nativen Grenze, Freigabe unter Last, Einstellungen aus einem zweiten
/// Thread, Parametergrenzen und Allokationsfreiheit der Sonderpfade. Deterministisch: feste Seeds,
/// selbst synthetisierte Signale, keine Audiogeräte.
/// </summary>
internal static class AnbindungGegenpruefungTests
{
    private static readonly (string Name, float Wert)[] Gifte =
    [
        ("NaN", float.NaN),
        ("+Inf", float.PositiveInfinity),
        ("-Inf", float.NegativeInfinity),
        ("1e30", 1e30f),
        ("-MaxValue", -float.MaxValue),
    ];

    private static bool AlleEndlich(ReadOnlySpan<float> x)
    {
        foreach (float v in x) if (!float.IsFinite(v)) return false;
        return true;
    }

    private static int Subnormale(ReadOnlySpan<float> x)
    {
        int n = 0;
        foreach (float v in x) if (float.IsSubnormal(v)) n++;
        return n;
    }

    private static string Z(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ NaN, Unendlich, Riesenwerte

    [Test]
    public static void Rnnoise_NichtEndlicheUndRiesigeWerte_VergiftenNicht()
    {
        // Ohne Schutz: ein NaN, Inf oder 1e30 in einem Rahmen, und RNNoise liefert danach nur noch NaN (gemessen).
        const int R = RnnoiseProzessor.RahmenSamples;
        var sprache = AnbindungSignale.Sprache(R * 120, 5);
        foreach (var (name, gift) in Gifte)
        {
            using var mitGift = new RnnoiseProzessor();
            using var referenz = new RnnoiseProzessor();
            var a = new float[R];
            var b = new float[R];
            for (int f = 0; f < 120; f++)
            {
                var rahmen = sprache.AsSpan(f * R, R).ToArray();
                var bereinigt = (float[])rahmen.Clone();
                if (f == 40)
                {
                    // Was die Hülle daraus machen soll: NaN/Inf -> 0, endlich -> auf [-1, 1] begrenzt.
                    float soll = float.IsFinite(gift) ? Math.Clamp(gift, -1f, 1f) : 0f;
                    foreach (int i in new[] { 0, 100, 479 })
                    {
                        rahmen[i] = gift;
                        bereinigt[i] = soll;
                    }
                }
                float pa = mitGift.Verarbeiten(rahmen, a);
                float pb = referenz.Verarbeiten(bereinigt, b);
                Pruefe.Wahr(pa is >= 0f and <= 1f, $"{name}, Rahmen {f}: Wahrscheinlichkeit {pa}");
                Pruefe.Wahr(AlleEndlich(a), $"{name}, Rahmen {f}: Ausgabe endlich");
                Pruefe.Gleich(pb, pa, $"{name}, Rahmen {f}: Wahrscheinlichkeit wie mit bereinigtem Eingang");
                Pruefe.Wahr(a.AsSpan().SequenceEqual(b), $"{name}, Rahmen {f}: bitgleich mit bereinigtem Eingang");
            }
            Pruefe.Gleich(float.IsFinite(gift) ? 0L : 3L, mitGift.ErsetzteWerte, $"{name}: ersetzte Werte");
        }
    }

    [Test]
    public static void Echo_NichtEndlicheWerte_VergiftenNicht()
    {
        // Ohne Schutz: ein NaN in Aufnahme ODER Wiedergabe, und AEC3 liefert danach nur noch NaN (gemessen).
        const int R = 480;
        int n = AnbindungSignale.Fs * 5, verzug = 48 * 40;
        var fern = AnbindungSignale.Sprache(n, 22, -18);
        var nah = AnbindungSignale.Sprache(n, 23, -24);
        foreach (var (name, gift, inAufnahme) in new[]
        {
            ("NaN Aufnahme", float.NaN, true),
            ("NaN Wiedergabe", float.NaN, false),
            ("+Inf Aufnahme", float.PositiveInfinity, true),
            ("-Inf Wiedergabe", float.NegativeInfinity, false),
        })
        {
            using var mitGift = new EchoProzessor();
            using var referenz = new EchoProzessor();
            foreach (var apm in new[] { mitGift, referenz })
            {
                apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
                apm.SetzeVerzoegerung(40);
            }
            var w = new float[R];
            var wRef = new float[R];
            var a = new float[R];
            var aRef = new float[R];
            for (int f = 0; f < n / R; f++)
            {
                fern.AsSpan(f * R, R).CopyTo(w);
                fern.AsSpan(f * R, R).CopyTo(wRef);
                for (int i = 0; i < R; i++)
                {
                    int t = f * R + i;
                    a[i] = nah[t] + (t >= verzug ? 0.5f * fern[t - verzug] : 0f);
                }
                a.CopyTo(aRef);
                if (f == 150)
                {
                    if (inAufnahme) { a[7] = gift; aRef[7] = 0f; }
                    else { w[7] = gift; wRef[7] = 0f; }
                }
                Pruefe.Gleich(0, mitGift.VerarbeiteWiedergabe(w), $"{name}, Rahmen {f}: Wiedergabe");
                referenz.VerarbeiteWiedergabe(wRef);
                if (f == 150 && !inAufnahme) Pruefe.Wahr(float.IsInfinity(w[7]) || float.IsNaN(w[7]), $"{name}: Wiedergaberahmen des Aufrufers unverändert");
                Pruefe.Gleich(0, mitGift.VerarbeiteAufnahme(a), $"{name}, Rahmen {f}: Aufnahme");
                referenz.VerarbeiteAufnahme(aRef);
                Pruefe.Wahr(AlleEndlich(a), $"{name}, Rahmen {f}: Ausgabe endlich");
                Pruefe.Wahr(a.AsSpan().SequenceEqual(aRef), $"{name}, Rahmen {f}: bitgleich mit bereinigtem Eingang");
            }
            Pruefe.Gleich(1L, mitGift.ErsetzteWerte, $"{name}: ersetzte Werte");
            var s = mitGift.Statistik();
            Pruefe.Wahr(s.Gueltig && float.IsFinite(s.Erle) && float.IsFinite(s.Erl), $"{name}: Statistik gültig, ERLE {s.Erle}, ERL {s.Erl}");
        }
    }

    [Test]
    public static void SoftClip_NichtEndlich_WirdNullAusgabeImmerBegrenzt()
    {
        // libopus lässt NaN durch; hinter dem Soft-Clip kommt nur noch das Ausgabegerät.
        var laut = AnbindungSignale.Sinus(960, 440, 1.8);
        var mitGift = new OpusSoftClip(2);
        var referenz = new OpusSoftClip(2);
        for (int f = 0; f < 4; f++)
        {
            var x = new float[1920];
            for (int i = 0; i < 960; i++) { x[2 * i] = laut[i]; x[2 * i + 1] = -0.5f * laut[i]; }
            var y = (float[])x.Clone();
            if (f == 1)
            {
                x[10] = float.NaN; x[11] = float.PositiveInfinity; x[501] = float.NegativeInfinity; x[1000] = float.NaN;
                y[10] = 0f; y[11] = 0f; y[501] = 0f; y[1000] = 0f;
            }
            mitGift.Anwenden(x);
            referenz.Anwenden(y);
            Pruefe.Wahr(x.All(v => float.IsFinite(v) && Math.Abs(v) <= 1f), $"Rahmen {f}: endlich und in [-1, 1]");
            Pruefe.Wahr(x.AsSpan().SequenceEqual(y), $"Rahmen {f}: bitgleich mit bereinigtem Eingang");
        }
        Pruefe.Gleich(4L, mitGift.ErsetzteWerte, "ersetzte Werte");

        // Riesige endliche Werte begrenzt libopus selbst auf ±2 und formt dann weich.
        var riesig = new float[960];
        riesig.AsSpan().Fill(1e30f);
        riesig[5] = -float.MaxValue;
        new OpusSoftClip().Anwenden(riesig);
        Pruefe.Wahr(riesig.All(v => float.IsFinite(v) && Math.Abs(v) <= 1f), "1e30 und -MaxValue begrenzt");
    }

    [Test]
    public static void Opus_NichtEndlicherRahmen_WirdStill()
    {
        // libopus 1.6.1 prüft im Float-Pfad selbst (opus_encoder.c: Energiesumme ab 1e9 oder NaN -> Rahmen
        // leeren, Hochpass zurücksetzen). Dieser Test hält das fest, weil die Hülle sich darauf verlässt.
        const int R = OpusKodierer.RahmenSamples;
        var sprache = AnbindungSignale.Sprache(R * 100, 5);
        var referenz = new float[sprache.Length];
        using (var k = new OpusKodierer())
        using (var d = new OpusDekodierer())
        {
            var paket = new byte[OpusKodierer.MaxPaketBytes];
            for (int f = 0; f < 100; f++) d.Dekodieren(paket.AsSpan(0, k.Kodieren(sprache.AsSpan(f * R, R), paket)), referenz.AsSpan(f * R, R));
        }
        double korrRef = AnbindungSignale.Korrelation(sprache.AsSpan(40 * R, 50 * R), referenz.AsSpan(40 * R + 312, 50 * R));

        foreach (var (name, gift) in Gifte)
        {
            using var k = new OpusKodierer();
            using var d = new OpusDekodierer();
            var paket = new byte[OpusKodierer.MaxPaketBytes];
            var aus = new float[sprache.Length];
            for (int f = 0; f < 100; f++)
            {
                var rahmen = sprache.AsSpan(f * R, R).ToArray();
                if (f == 30) rahmen[100] = gift;
                int n = k.Kodieren(rahmen, paket);
                Pruefe.Wahr(n is > 0 and <= OpusKodierer.MaxPaketBytes, $"{name}, Rahmen {f}: Paketlänge {n}");
                Pruefe.Gleich(R, d.Dekodieren(paket.AsSpan(0, n), aus.AsSpan(f * R, R)), $"{name}, Rahmen {f}: Samples");
            }
            Pruefe.Wahr(AlleEndlich(aus), $"{name}: Ausgabe endlich");
            double pegel = AnbindungSignale.PegelDb(aus.AsSpan(30 * R + 312, R));
            double korr = AnbindungSignale.Korrelation(sprache.AsSpan(40 * R, 50 * R), aus.AsSpan(40 * R + 312, 50 * R));
            Console.WriteLine($"MESSUNG Opus Giftrahmen {name}: Pegel {Z(pegel)} dBFS, Korrelation danach {Z(korr)} (ohne Gift {Z(korrRef)}), Abstand zur Referenz {Z(AnbindungSignale.AbstandDb(referenz.AsSpan(40 * R, 50 * R), aus.AsSpan(40 * R, 50 * R)))} dB");
            Pruefe.Wahr(pegel < -40, $"{name}: Giftrahmen still ({pegel:0.0} dBFS)");
            Pruefe.Wahr(korr > korrRef - 0.02, $"{name}: danach wie ohne Gift (Korrelation {korr:0.000} gegen {korrRef:0.000})");
        }
    }

    [Test]
    public static void Resampler_NaN_KlingtNachFilterlaengeAb()
    {
        // Der Resampler wird nicht bereinigt: sein Filter ist nicht rekursiv. Festgehalten wird, dass NaN
        // nur für die Filterlänge im Ausgang steht und danach verschwindet.
        using var r = new Resampler(48000, 16000);
        var ein = AnbindungSignale.Sinus(48000, 440, 0.5);
        ein[4800] = float.NaN;
        var aus = new List<float>();
        var puffer = new float[1024];
        for (int f = 0; f < 100; f++)
        {
            r.Verarbeiten(ein.AsSpan(f * 480, 480), puffer, out int verbraucht, out int erzeugt);
            Pruefe.Gleich(480, verbraucht, "alles verbraucht");
            aus.AddRange(puffer.AsSpan(0, erzeugt).ToArray());
        }
        var nan = Enumerable.Range(0, aus.Count).Where(i => !float.IsFinite(aus[i])).ToList();
        Pruefe.Wahr(nan.Count > 0, "NaN erscheint (Filter nicht bereinigt)");
        Pruefe.Wahr(nan.Count <= 2 * r.AusgangsLatenz, $"höchstens die Filterlänge betroffen: {nan.Count} Werte");
        Pruefe.Wahr(nan[^1] <= 1600 + 2 * r.AusgangsLatenz, $"letzter NaN-Wert bei {nan[^1]}");
    }

    // ------------------------------------------------------------------ Denormale

    [Test]
    public static void Anbindung_StilleNachSprache_KeineSubnormalenWerte()
    {
        // In digitaler Stille klingen die rekursiven Filter von RNNoise und APM bis in den subnormalen
        // Bereich ab (vorher gemessen: 8663 bzw. 1753 Werte in 10 s). Die Hüllen setzen sie auf 0.
        var sprache = AnbindungSignale.Sprache(48000, 5);
        using (var rn = new RnnoiseProzessor())
        {
            var aus = new float[480];
            var t = new List<double>();
            int sub = 0;
            for (int f = 0; f < 1100; f++)
            {
                ReadOnlySpan<float> ein = f < 100 ? sprache.AsSpan(f * 480, 480) : new float[480];
                long a = Stopwatch.GetTimestamp();
                float p = rn.Verarbeiten(ein, aus);
                if (f >= 100) t.Add(Stopwatch.GetElapsedTime(a).TotalMilliseconds);
                sub += Subnormale(aus);
                Pruefe.Wahr(AlleEndlich(aus), $"RNNoise Rahmen {f} endlich");
                // Der Hochpass von RNNoise (Pol bei 0,998) klingt über einige hundert ms aus.
                if (f >= 150) Pruefe.Gleich(0f, p, $"RNNoise Rahmen {f}: Wahrscheinlichkeit in Stille");
            }
            Console.WriteLine($"MESSUNG RNNoise Stille nach Sprache: Mittel {Z(t.Average())} ms, Max {Z(t.Max())} ms");
            Pruefe.Gleich(0, sub, "RNNoise: subnormale Ausgabewerte");
            Pruefe.Wahr(t.Average() < 1.0, "RNNoise in Stille unter 1 ms je Rahmen");
        }
        using (var apm = new EchoProzessor())
        {
            apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
            var still = new float[480];
            var t = new List<double>();
            int sub = 0;
            for (int f = 0; f < 1100; f++)
            {
                var a = f < 100 ? sprache.AsSpan(f * 480, 480).ToArray() : new float[480];
                long s = Stopwatch.GetTimestamp();
                apm.VerarbeiteWiedergabe(f < 100 ? sprache.AsSpan(f * 480, 480) : still);
                apm.VerarbeiteAufnahme(a);
                if (f >= 100) t.Add(Stopwatch.GetElapsedTime(s).TotalMilliseconds);
                sub += Subnormale(a);
                Pruefe.Wahr(AlleEndlich(a), $"APM Rahmen {f} endlich");
            }
            Console.WriteLine($"MESSUNG APM Stille nach Sprache: Mittel {Z(t.Average())} ms, Max {Z(t.Max())} ms");
            Pruefe.Gleich(0, sub, "APM: subnormale Ausgabewerte");
            Pruefe.Wahr(t.Average() < 1.0, "APM in Stille unter 1 ms je Rahmen");
        }

        // Subnormale Eingänge selbst: RNNoise macht daraus Stille, das APM bleibt endlich.
        using (var rn = new RnnoiseProzessor())
        {
            var winzig = new float[480];
            for (int i = 0; i < 480; i++) winzig[i] = (i % 2 == 0 ? 1 : -1) * 1e-40f;
            var aus = new float[480];
            for (int f = 0; f < 20; f++) Pruefe.Gleich(0f, rn.Verarbeiten(winzig, aus), "1e-40: Wahrscheinlichkeit 0");
            Pruefe.Wahr(aus.All(v => v == 0f), "1e-40: Ausgabe 0");
        }
    }

    // ------------------------------------------------------------------ Opus an der nativen Grenze

    [Test]
    public static void Opus_Zufallspakete_KeinAbsturzImmerEndlich()
    {
        // Pakete kommen vom Netz. libopus muss beliebige Bytes aushalten, die Hülle muss jede Rückgabe in
        // den Grenzen des Ziels halten und immer endliche Samples liefern.
        using var d = new OpusDekodierer();
        var z = new AnbindungSignale.Zufall(99);
        var gross = new float[OpusDekodierer.MaxSamples];
        var klein = new float[OpusDekodierer.RahmenSamples];
        var fec = new float[OpusDekodierer.RahmenSamples];
        var paket = new byte[1500];
        var laengen = new HashSet<int>();
        for (int i = 0; i < 4000; i++)
        {
            int len = 1 + (int)(z.Naechste() % 1400);
            for (int j = 0; j < len; j++) paket[j] = (byte)z.Naechste();
            var p = paket.AsSpan(0, len);
            var ziel = i % 2 == 0 ? gross : klein;
            int n = d.Dekodieren(p, ziel);
            Pruefe.Wahr(n > 0 && n <= ziel.Length && n % OpusDekodierer.RasterSamples == 0, $"Paket {i}: {n} Samples bei Ziel {ziel.Length}");
            Pruefe.Wahr(AlleEndlich(ziel.AsSpan(0, n)), $"Paket {i}: endlich");
            laengen.Add(n);
            Pruefe.Gleich(OpusDekodierer.RahmenSamples, d.DekodierenFec(p, fec, OpusDekodierer.RahmenSamples), $"Paket {i}: FEC-Länge");
            Pruefe.Wahr(AlleEndlich(fec), $"Paket {i}: FEC endlich");
            int s = OpusPaket.AnzahlSamples(p);
            Pruefe.Wahr(s < 0 || (s % OpusDekodierer.RasterSamples == 0 && s is > 0 and <= OpusDekodierer.MaxSamples), $"Paket {i}: AnzahlSamples {s}");
            // libopus allein meldete hier bis zu 63 Rahmen (Code 3, ungeprüft); die Hülle nur gültige.
            int rahmen = OpusPaket.AnzahlRahmen(p);
            Pruefe.Wahr(rahmen < 0 || rahmen is >= 1 and <= 48, $"Paket {i}: AnzahlRahmen {rahmen}");
            Pruefe.Wahr((rahmen < 0) == (s < 0), $"Paket {i}: Rahmen und Samples gleich gültig ({rahmen}, {s})");
            _ = OpusPaket.HatLbrr(p);
            _ = OpusPaket.Toc(p).SamplesJeRahmen;
        }
        Pruefe.Wahr(d.DekodiertePakete > 1000 && d.UngueltigePakete > 1000, $"beide Wege benutzt: {d.DekodiertePakete} dekodiert, {d.UngueltigePakete} ungültig");
        Pruefe.Wahr(laengen.Contains(OpusDekodierer.MaxSamples), "auch 120-ms-Pakete dekodiert");
    }

    /// <summary>Baut aus einem 20-ms-Paket (Code 0) ein 40-ms-Paket mit zwei gleichen Rahmen (Code 1, RFC 6716 3.2.2).</summary>
    private static int Doppeln(ReadOnlySpan<byte> einzeln, Span<byte> ziel)
    {
        int nutz = einzeln.Length - 1;
        ziel[0] = (byte)((einzeln[0] & 0xFC) | 1);
        einzeln[1..].CopyTo(ziel[1..]);
        einzeln[1..].CopyTo(ziel[(1 + nutz)..]);
        return 1 + 2 * nutz;
    }

    [Test]
    public static void Opus_Paket40ms_ZielZuKlein_WirdVerdecktNichtUeberschrieben()
    {
        const int R = OpusKodierer.RahmenSamples;
        var sprache = AnbindungSignale.Sprache(R * 20, 17);
        using var k = new OpusKodierer();
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        var doppel = new byte[2 * OpusKodierer.MaxPaketBytes];
        for (int f = 0; f < 19; f++) k.Kodieren(sprache.AsSpan(f * R, R), paket);
        int n = k.Kodieren(sprache.AsSpan(19 * R, R), paket);
        int lang = Doppeln(paket.AsSpan(0, n), doppel);
        var p = doppel.AsSpan(0, lang);
        Pruefe.Gleich(2 * R, OpusPaket.AnzahlSamples(p), "40 ms laut libopus");
        Pruefe.Gleich(2, OpusPaket.AnzahlRahmen(p), "zwei Rahmen");

        using var d = new OpusDekodierer();
        var gross = new float[2 * R];
        Pruefe.Gleich(2 * R, d.Dekodieren(p, gross), "passendes Ziel: 40 ms");
        Pruefe.Gleich(0L, d.UngueltigePakete, "gültig");

        // Wächter hinter dem Ziel: libopus darf nicht über die übergebene Länge schreiben.
        var mitWaechter = new float[R + 64];
        mitWaechter.AsSpan(R).Fill(123f);
        Pruefe.Gleich(R, d.Dekodieren(p, mitWaechter.AsSpan(0, R)), "zu kleines Ziel: 20 ms Verdeckung");
        Pruefe.Gleich(1L, d.UngueltigePakete, "gezählt");
        Pruefe.Gleich(-2, d.LetzterFehler, "OPUS_BUFFER_TOO_SMALL");
        Pruefe.Wahr(mitWaechter.AsSpan(R).ToArray().All(v => v == 123f), "nichts hinter das Ziel geschrieben");

        // Dasselbe für PLC und FEC: genau fehlendeSamples, keinen Wert mehr.
        mitWaechter.AsSpan(R).Fill(123f);
        Pruefe.Gleich(R, d.Verdecken(mitWaechter.AsSpan(0, R), R), "PLC");
        Pruefe.Gleich(R, d.DekodierenFec(p, mitWaechter.AsSpan(0, R), R), "FEC mit 40-ms-Folgepaket");
        Pruefe.Wahr(mitWaechter.AsSpan(R).ToArray().All(v => v == 123f), "PLC/FEC: nichts hinter das Ziel geschrieben");
    }

    // ------------------------------------------------------------------ Threads und Lebensdauer

    /// <summary>
    /// Ein Arbeits-Thread verarbeitet ohne Pause, der Test-Thread gibt nach 50 Runden frei. Erwartet:
    /// Der Arbeiter endet mit <see cref="ObjectDisposedException"/>, nie mit einer Zugriffsverletzung
    /// (die würde den Prozess beenden). SafeHandle hält das native Objekt, solange ein Aufruf läuft.
    /// </summary>
    private static void FreigabeUnterLast(string name, IDisposable ziel, Action runde)
    {
        Exception? fehler = null;
        long runden = 0;
        using var angelaufen = new ManualResetEventSlim();
        var arbeiter = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    runde();
                    if (Interlocked.Increment(ref runden) == 50) angelaufen.Set();
                }
            }
            catch (Exception e)
            {
                fehler = e;
            }
            finally
            {
                try { angelaufen.Set(); }
                catch (ObjectDisposedException) { /* nur wenn der Test schon abgebrochen ist */ }
            }
        })
        { IsBackground = true, Name = "Gegenprüfung " + name };
        arbeiter.Start();
        angelaufen.Wait();
        ziel.Dispose();
        Pruefe.Wahr(arbeiter.Join(TimeSpan.FromSeconds(20)), $"{name}: Arbeiter endet");
        Pruefe.Wahr(fehler is ObjectDisposedException, $"{name}: Ende durch ObjectDisposedException, war {fehler?.GetType().Name}: {fehler?.Message}");
        Pruefe.Wahr(Interlocked.Read(ref runden) >= 50, $"{name}: {runden} Runden vor der Freigabe");
        ziel.Dispose();
    }

    [Test]
    public static void Anbindung_FreigabeWaehrendVerarbeitung_OhneAbsturz()
    {
        var sprache = AnbindungSignale.Sprache(960 * 10, 51);

        var k = new OpusKodierer();
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        FreigabeUnterLast("Kodierer", k, () => k.Kodieren(sprache.AsSpan(0, 960), paket));

        var d = new OpusDekodierer();
        var pcm = new float[960];
        using (var k2 = new OpusKodierer())
        {
            int n = k2.Kodieren(sprache.AsSpan(0, 960), paket);
            var p = paket.AsSpan(0, n).ToArray();
            FreigabeUnterLast("Dekodierer", d, () => { d.Dekodieren(p, pcm); d.Verdecken(pcm, 480); });
        }

        var rn = new RnnoiseProzessor();
        var r480 = new float[480];
        FreigabeUnterLast("RNNoise", rn, () => rn.Verarbeiten(sprache.AsSpan(0, 480), r480));

        var rs = new Resampler(44100, 48000, kanaele: 2);
        var aus = new float[2048];
        FreigabeUnterLast("Resampler", rs, () => rs.Verarbeiten(sprache.AsSpan(0, 882), aus, out _, out _));

        // APM: Aufnahme und Wiedergabe gleichzeitig aus zwei Threads, Freigabe mitten hinein.
        var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        Exception? fehlerWiedergabe = null;
        var wiedergabe = new Thread(() =>
        {
            try { while (true) apm.VerarbeiteWiedergabe(sprache.AsSpan(480, 480)); }
            catch (Exception e) { fehlerWiedergabe = e; }
        })
        { IsBackground = true };
        wiedergabe.Start();
        var a = new float[480];
        FreigabeUnterLast("APM", apm, () => { sprache.AsSpan(0, 480).CopyTo(a); apm.VerarbeiteAufnahme(a); _ = apm.Statistik(); });
        Pruefe.Wahr(wiedergabe.Join(TimeSpan.FromSeconds(20)), "APM-Wiedergabe endet");
        Pruefe.Wahr(fehlerWiedergabe is ObjectDisposedException, $"APM-Wiedergabe: Ende durch ObjectDisposedException, war {fehlerWiedergabe?.GetType().Name}");
    }

    [Test]
    public static void Echo_AufnahmeUndWiedergabeParallel_EndlichUndGezaehlt()
    {
        const int Rahmen = 600;
        var fern = AnbindungSignale.Sprache(480 * Rahmen, 61, -18);
        var nah = AnbindungSignale.Sprache(480 * Rahmen, 62, -24);
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        apm.SetzeVerzoegerung(40);
        int fehlerWiedergabe = 0, statistikNaN = 0;
        Exception? ausnahme = null;
        var wiedergabe = new Thread(() =>
        {
            try
            {
                for (int f = 0; f < Rahmen; f++)
                    if (apm.VerarbeiteWiedergabe(fern.AsSpan(f * 480, 480)) != 0) fehlerWiedergabe++;
            }
            catch (Exception e) { ausnahme = e; }
        });
        var statistik = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 20000; i++)
                {
                    var s = apm.Statistik();
                    if (s.Gueltig && !(float.IsFinite(s.Erle) && float.IsFinite(s.Erl))) statistikNaN++;
                    apm.SetzeVerzoegerung(40 + (i & 1));
                }
            }
            catch (Exception e) { ausnahme = e; }
        });
        wiedergabe.Start();
        statistik.Start();
        var a = new float[480];
        for (int f = 0; f < Rahmen; f++)
        {
            nah.AsSpan(f * 480, 480).CopyTo(a);
            Pruefe.Gleich(0, apm.VerarbeiteAufnahme(a), $"Aufnahme {f}");
            Pruefe.Wahr(AlleEndlich(a), $"Aufnahme {f} endlich");
        }
        wiedergabe.Join();
        statistik.Join();
        Pruefe.Wahr(ausnahme is null, $"keine Ausnahme in den Nebenthreads: {ausnahme?.Message}");
        Pruefe.Gleich(0, fehlerWiedergabe, "Wiedergabe-Fehlercodes");
        Pruefe.Gleich(0, statistikNaN, "gültige Statistik immer endlich");
        Pruefe.Gleich((long)Rahmen, apm.AufnahmeRahmen, "Aufnahmerahmen");
        Pruefe.Gleich((long)Rahmen, apm.WiedergabeRahmen, "Wiedergaberahmen");
    }

    [Test]
    public static void Opus_EinstellungenAusZweitemThread_WirkenOhneSperre()
    {
        const int R = OpusKodierer.RahmenSamples;
        var sprache = AnbindungSignale.Sprache(R * 200, 9);
        using var k = new OpusKodierer();
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        Exception? fehler = null;
        using var los = new ManualResetEventSlim();
        var steller = new Thread(() =>
        {
            try
            {
                var z = new AnbindungSignale.Zufall(5);
                los.Wait();
                for (int i = 0; i < 20000; i++)
                {
                    k.SetzeVerlustHinweis((int)(z.Naechste() % 130) - 10);
                    k.SetzeBitrate(32000 + (int)(z.Naechste() % 32001));
                }
            }
            catch (Exception e)
            {
                fehler = e;
            }
        });
        steller.Start();
        los.Set();
        for (int f = 0; f < 200; f++)
        {
            int n = k.Kodieren(sprache.AsSpan(f * R, R), paket);
            Pruefe.Wahr(n is > 0 and <= OpusKodierer.MaxPaketBytes, $"Rahmen {f}: {n} Byte");
        }
        steller.Join();
        Pruefe.Wahr(fehler is null, $"Steller ohne Fehler: {fehler?.Message}");

        k.SetzeVerlustHinweis(23);
        k.SetzeBitrate(40000);
        k.Kodieren(sprache.AsSpan(0, R), paket);
        var e = k.EinstellungenLesen();
        Pruefe.Gleich(23, e.VerlustHinweis, "letzter Verlusthinweis übernommen");
        Pruefe.Gleich(40000, e.Bitrate, "letzte Bitrate übernommen");
    }

    // ------------------------------------------------------------------ Parametergrenzen

    [Test]
    public static void Resampler_Grenzen_GegenRiesenfilter()
    {
        // Vorher legte etwa 400 MHz zu 8 kHz nativ einen Filter mit 4 Mio. Koeffizienten an.
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(7999, 48000).Dispose(), "unter 8 kHz");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(48000, 384001).Dispose(), "über 384 kHz");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new Resampler(400_000_000, 8000).Dispose(), "400 MHz");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Resampler.Fraktional(49, 1, 48000, 48000).Dispose(), "Verhältnis 49");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Resampler.Fraktional(1, 49, 48000, 48000).Dispose(), "Verhältnis 1/49");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Resampler.Fraktional(uint.MaxValue, 1, 48000, 48000).Dispose(), "Verhältnis uint.MaxValue");

        using var r = new Resampler(384000, 8000, 10);
        Pruefe.Gleich((48u, 1u), r.Verhaeltnis, "384 kHz zu 8 kHz erlaubt");
        using var hoch = new Resampler(8000, 384000, 10);
        Pruefe.Gleich((1u, 48u), hoch.Verhaeltnis, "8 kHz zu 384 kHz erlaubt");
        using var f48 = Resampler.Fraktional(48, 1, 384000, 8000);
        Pruefe.Gleich((48u, 1u), f48.Verhaeltnis, "fraktional 48 erlaubt");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => r.SetzeRate(48000, 1000), "SetzeRate unter 8 kHz");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => r.SetzeRateFraktional(400000, 1, 48000, 48000), "SetzeRateFraktional Verhältnis 400000");
        Pruefe.Gleich((48u, 1u), r.Verhaeltnis, "nach abgelehnten Aufrufen unverändert");
    }

    // ------------------------------------------------------------------ Echtzeitregel

    [Test]
    public static void Anbindung_Sonderpfade_AllokierenNicht()
    {
        // Ergänzt Anbindung_HeissePfade_AllokierenNicht um die seltenen Wege: ungültige, leere und zu
        // lange Pakete, FEC ohne Paket, NaN/Inf-Bereinigung, Stereo-Resampler, Driftkorrektur, Soft-Clip.
        var sprache = AnbindungSignale.Sprache(48000, 72);
        using var k = new OpusKodierer();
        using var d = new OpusDekodierer();
        using var rn = new RnnoiseProzessor();
        using var rsStereo = new Resampler(44100, 48000, 5, kanaele: 2);
        using var rsDrift = Resampler.Fraktional(480000, 480048, 48000, 48005);
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        var clip = new OpusSoftClip(2);
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        var doppel = new byte[2 * OpusKodierer.MaxPaketBytes];
        var kaputt = new byte[] { 0x7B, 0x00 };
        var pcm = new float[OpusDekodierer.MaxSamples];
        var klein = new float[OpusDekodierer.RahmenSamples];
        var r480 = new float[480];
        var w480 = new float[480];
        var stereoEin = new float[882];
        var aus = new float[2048];

        void Runde(int i)
        {
            int f = (i % 48) * 960;
            k.SetzeBitrate(i % 2 == 0 ? 32000 : 48000);
            k.SetzeVerlustHinweis(i % 3 * 10);
            int n = k.Kodieren(sprache.AsSpan(f, 960), paket);
            int lang = Doppeln(paket.AsSpan(0, n), doppel);
            d.Dekodieren(doppel.AsSpan(0, lang), pcm);
            d.Dekodieren(doppel.AsSpan(0, lang), klein);
            d.Dekodieren(kaputt, klein);
            d.Dekodieren([], klein);
            d.DekodierenFec([], klein, 960);
            d.DekodierenFec(kaputt, klein, 960);
            d.DekodierenFec(paket.AsSpan(0, n), pcm, 1920);
            _ = d.LetzteDauer;
            _ = OpusPaket.AnzahlSamples(doppel.AsSpan(0, lang));
            _ = OpusPaket.AnzahlRahmen(doppel.AsSpan(0, lang));

            sprache.AsSpan(f, 480).CopyTo(r480);
            r480[3] = float.NaN;
            r480[4] = float.PositiveInfinity;
            r480[5] = 1e30f;
            rn.Verarbeiten(r480);
            sprache.AsSpan(f + 480, 480).CopyTo(w480);
            w480[9] = float.NaN;
            apm.VerarbeiteWiedergabe(w480);
            sprache.AsSpan(f, 480).CopyTo(r480);
            r480[1] = float.NegativeInfinity;
            apm.VerarbeiteAufnahme(r480);
            _ = apm.Statistik();

            for (int j = 0; j < 441; j++)
            {
                stereoEin[2 * j] = sprache[f + j];
                stereoEin[2 * j + 1] = -sprache[f + j];
            }
            rsStereo.Verarbeiten(stereoEin, aus, out _, out _);
            rsDrift.SetzeRateFraktional(480000, (i & 1) == 0 ? 480048u : 479952u, 48000, 48005);
            rsDrift.Verarbeiten(sprache.AsSpan(f, 480), aus, out _, out _);

            pcm[0] = float.NaN;
            pcm[1] = 3f;
            clip.Anwenden(pcm.AsSpan(0, 1920));
        }

        for (int i = 0; i < 20; i++) Runde(i);
        long vorher = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 20; i < 220; i++) Runde(i);
        long allokiert = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich(0L, allokiert, "allokierte Bytes in 200 Runden der Sonderpfade");
        // Je Runde ungültig: 40 ms in zu kleines Ziel, kaputt, leer, FEC mit kaputtem Folgepaket.
        Pruefe.Gleich(4L * 220, d.UngueltigePakete, "Sonderpfade wirklich durchlaufen");
        Pruefe.Gleich(2L * 220, rn.ErsetzteWerte, "RNNoise: NaN und Inf ersetzt (1e30 wird nur begrenzt)");
        Pruefe.Gleich(2L * 220, apm.ErsetzteWerte, "APM: je ein Wert in Aufnahme und Wiedergabe ersetzt");
        Pruefe.Gleich(220L, clip.ErsetzteWerte, "Soft-Clip: NaN ersetzt");
    }
}
