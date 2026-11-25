// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen der Klangstufe B, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben die Zahlen für die Wahl zwischen RNNoise
/// und DeepFilterNet3 aus (Recherche Abschnitt 2 und 8): Laufzeit je 10-ms-Rahmen mit und ohne künstliche Last, Latenz, Speicher,
/// und je Störgeräusch (<see cref="Stoergeraeusche"/>) Restrauschen in den Pausen, Pegel und Treue der Sprache, gedämpfte
/// Wortanfänge und Satzenden hinter dem Filter. Eingang ist der Klangkorpus mit Mikrofon 0 dB und Grundrauschen -65 dBFS
/// (<see cref="Pumpmessung.Eingang"/>) plus das Störgeräusch, durch den Hochpass des APM wie in der Kette vor jedem Rauschfilter;
/// Bezug ist der Korpus allein durch denselben Hochpass.
/// </summary>
internal static class MessungKlangB
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const int R = 480;

    /// <summary>DeepFilter mit den Schwellen von libDF, DeepFilterLadspa wie das LADSPA-Plugin, DeepFilterVoll jeden Rahmen voll.</summary>
    internal enum Filter { Aus, Rnnoise, DeepFilter, DeepFilterLadspa, DeepFilterVoll }

    private static readonly Lazy<(float[] Sauber, int Versatz)> Bezug = new(() =>
    {
        var sauber = Hochpass(Klangkorpus.Signal);
        var versatz = AnbindungSignale.BesterVerzug(Klangkorpus.Signal, sauber, Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 2400).Verzug;
        return (sauber, versatz);
    });

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Klangstufe B übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    internal static int Verzug(Filter f) => f switch
    {
        Filter.Rnnoise => RnnoiseProzessor.VerzoegerungSamples,
        Filter.DeepFilter or Filter.DeepFilterLadspa or Filter.DeepFilterVoll => DeepFilterProzessor.VerzoegerungSamples,
        _ => 0,
    };

    /// <summary>Ein Filter rahmenweise über das ganze Signal im aufrufenden Thread; Ausgang (unverschoben) und Zeit je Rahmen in µs.</summary>
    internal static (float[] Aus, double[] ZeitUs) Filtern(Filter f, float[] ein)
    {
        var aus = new float[ein.Length / R * R];
        var zeiten = new double[ein.Length / R];
        using var rn = f == Filter.Rnnoise ? new RnnoiseProzessor() : null;
        using var df = f switch
        {
            Filter.DeepFilter => new DeepFilterProzessor(),
            Filter.DeepFilterLadspa => new DeepFilterProzessor(schwellen: DeepFilterSchwellen.Ladspa),
            Filter.DeepFilterVoll => new DeepFilterProzessor(schwellen: DeepFilterSchwellen.Voll),
            _ => null,
        };
        for (var r = 0; r < zeiten.Length; r++)
        {
            var e = ein.AsSpan(r * R, R);
            var a = aus.AsSpan(r * R, R);
            var t = Stopwatch.GetTimestamp();
            if (rn is not null) rn.Verarbeiten(e, a);
            else if (df is not null) df.Verarbeiten(e, a);
            else e.CopyTo(a);
            zeiten[r] = (Stopwatch.GetTimestamp() - t) * 1e6 / Stopwatch.Frequency;
        }
        return (aus, zeiten);
    }

    /// <summary>Der Hochpass des APM, wie er in der Kette vor jedem Rauschfilter liegt (AEC3 erzwingt ihn, ohne Echounterdrückung bleibt er an).</summary>
    internal static float[] Hochpass(float[] x)
    {
        var y = (float[])x.Clone();
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.EchoUndHochpass with { Echounterdrueckung = false });
        for (var r = 0; r < y.Length / R; r++) _ = apm.VerarbeiteAufnahme(y.AsSpan(r * R, R));
        return y;
    }

    /// <summary>Szenen: Name und Störgeräusch über die Länge des Korpus.</summary>
    internal static (string Name, float[] Stoerung)[] Szenen()
    {
        var n = Klangkorpus.Signal.Length;
        float[] Summe(params float[][] teile)
        {
            var s = new float[n];
            foreach (var t in teile) for (var i = 0; i < n; i++) s[i] += t[i];
            return s;
        }
        return
        [
            ("ruhig (Grundrauschen -65 dBFS)", new float[n]),
            ("Lüfter rosa -50 dBFS", Stoergeraeusche.Rosa(n, -50, 11)),
            ("Lüfter braun -45 dBFS", Stoergeraeusche.Braun(n, -45, 12)),
            ("Tastatur -40 dBFS", Stoergeraeusche.Tastatur(n, -40, 13)),
            ("Stimmen -20 dB", Stoergeraeusche.Stimmen(n, -20)),
            ("Musik -40 dBFS", Stoergeraeusche.Musik(n, -40, 14)),
            ("alles zusammen", Summe(Stoergeraeusche.Rosa(n, -55, 15), Stoergeraeusche.Tastatur(n, -45, 16), Stoergeraeusche.Stimmen(n, -25), Stoergeraeusche.Musik(n, -45, 17))),
        ];
    }

    /// <summary>Korpus mit Mikrofon 0 dB, Grundrauschen und <paramref name="stoerung"/>, um <paramref name="db"/> verstärkt, durch den Hochpass.</summary>
    internal static float[] Mischen(float[] stoerung, float db = 0f)
    {
        var x = Pumpmessung.Eingang(0f);
        var f = Funk.Klang.Verarbeitung.Pegel.DbZuLinear(db);
        for (var i = 0; i < x.Length; i++) x[i] = f * (x[i] + stoerung[i]);
        return Hochpass(x);
    }

    /// <summary>Kennzahlen eines gefilterten Signals gegen den Korpus (zeitrichtig um <paramref name="verzug"/> verschoben).</summary>
    /// <param name="RestDbfs">Pegel in den Pausen (mehr als 300 ms von jeder Äußerung entfernt).</param>
    /// <param name="DaempfungDb">Eingang minus Ausgang in denselben Pausen.</param>
    /// <param name="SpracheDb">Ausgang gegen den sauberen Korpus in den aktiven Rahmen der Äußerungen (unter 0 = leiser).</param>
    /// <param name="SiSdrDb">Skalierungsfreier Signal-Verzerrungs-Abstand über alle Äußerungen (höher ist besser).</param>
    /// <param name="AnfaengeGedaempft">Wortanfänge, deren erste 30 ms mehr als 6 dB unter dem sauberen Korpus liegen.</param>
    /// <param name="EndenGedaempft">Satzenden, deren letzte 150 ms mehr als 6 dB unter dem sauberen Korpus liegen.</param>
    internal sealed record Kennzahlen(double RestDbfs, double DaempfungDb, double SpracheDb, double SiSdrDb, int Woerter, int AnfaengeGedaempft,
        int Saetze, int EndenGedaempft);

    /// <param name="ein">Eingang des Filters (Korpus, Störung, Hochpass), Zeitachse wie der Bezug.</param>
    /// <param name="aus">Ausgang des Filters, unverschoben.</param>
    /// <param name="verzug">Verzögerung des Filters in Samples.</param>
    /// <param name="db">Verstärkung, mit der <paramref name="ein"/> gebildet wurde; der Bezug wird ebenso verstärkt.</param>
    internal static Kennzahlen Bewerten(float[] ein, float[] aus, int verzug, float db = 0f)
    {
        var (bezug, versatz) = Bezug.Value;
        var g = Funk.Klang.Verarbeitung.Pegel.DbZuLinear(db);
        var sauber = Array.ConvertAll(bezug, v => v * g);
        var aeusserungen = Klangkorpus.Aeusserungen;
        var takte = aus.Length / R;
        double eEin = 0, eAus = 0;
        var nPause = 0;
        for (var f = 0; f < takte; f++)
        {
            var von = f * R;
            if (von + R + verzug > aus.Length || aeusserungen.Any(a => von < a.Ende + versatz + 14400 && von + R > a.Start + versatz - 14400)) continue;
            eEin += Energie(ein.AsSpan(von, R));
            eAus += Energie(aus.AsSpan(von + verzug, R));
            nPause += R;
        }
        double sAus = 0, sSauber = 0, ys = 0, ss = 0, yy = 0;
        int woerter = 0, anfaenge = 0, enden = 0;
        foreach (var a in aeusserungen)
        {
            var start = a.Start + versatz;
            var ende = Math.Min(a.Ende + versatz, aus.Length - verzug);
            var spitze = double.NegativeInfinity;
            for (var i = start; i + R <= ende; i += R) spitze = Math.Max(spitze, Db(Energie(sauber.AsSpan(i, R)) / R));
            for (var i = start; i + R <= ende; i += R)
            {
                var es = Energie(sauber.AsSpan(i, R));
                if (Db(es / R) < spitze - 25) continue;
                sSauber += es;
                sAus += Energie(aus.AsSpan(i + verzug, R));
            }
            for (var i = start; i < ende; i++)
            {
                double s = sauber[i], y = aus[i + verzug];
                ys += y * s;
                ss += s * s;
                yy += y * y;
            }
            foreach (var w0 in a.Woerter)
            {
                var w = w0 + versatz;
                if (w + 1440 + verzug > aus.Length) continue;
                woerter++;
                if (Energie(aus.AsSpan(w + verzug, 1440)) < 0.25 * Energie(sauber.AsSpan(w, 1440))) anfaenge++;
            }
            var e = a.Ende + versatz;
            if (e + verzug <= aus.Length && Energie(aus.AsSpan(e - 7200 + verzug, 7200)) < 0.25 * Energie(sauber.AsSpan(e - 7200, 7200))) enden++;
        }
        var alpha = ys / ss;
        var ziel = alpha * alpha * ss;
        var fehler = yy - 2 * alpha * ys + alpha * alpha * ss;
        return new Kennzahlen(Db(eAus / nPause), Db(eEin / nPause) - Db(eAus / nPause), Db(sAus / sSauber), 10 * Math.Log10(ziel / Math.Max(fehler, 1e-30)),
            woerter, anfaenge, aeusserungen.Count, enden);
    }

    private static double Energie(ReadOnlySpan<float> x)
    {
        double e = 0;
        foreach (var v in x) e += (double)v * v;
        return e;
    }

    private static double Db(double leistung) => 10 * Math.Log10(Math.Max(leistung, 1e-30));

    internal static string Verteilung(double[] zeiten)
    {
        var s = (double[])zeiten.Clone();
        Array.Sort(s);
        double Q(double q) => s[Math.Clamp((int)Math.Ceiling(q * s.Length) - 1, 0, s.Length - 1)];
        return string.Create(C, $"p50 {Q(0.5):0} p95 {Q(0.95):0} p99 {Q(0.99):0} max {s[^1]:0} µs, über 5 ms {s.Count(x => x > 5000)}, über 10 ms {s.Count(x => x > 10000)} von {s.Length}");
    }

    private static string Zeile(Filter f, Kennzahlen k) => string.Create(C,
        $"{f}: Rest {k.RestDbfs:0.0} dBFS (-{k.DaempfungDb:0.0} dB), Sprache {k.SpracheDb:+0.0;-0.0} dB, SI-SDR {k.SiSdrDb:0.0} dB, Anfänge gedämpft {k.AnfaengeGedaempft}/{k.Woerter}, Enden {k.EndenGedaempft}/{k.Saetze}");

    /// <summary>
    /// Qualität je Störgeräusch und Filter (Aus, RNNoise, DeepFilterNet3 in drei Schwellen) auf Ebene der Filter: Restrauschen in den
    /// Pausen, Dämpfung, Pegel und Treue der Sprache, gedämpfte Wortanfänge und Satzenden. Dazu die gemessene Verzögerung.
    /// </summary>
    [Test]
    public static void Messung_KlangB_Qualitaet()
    {
        if (Aus()) return;
        foreach (var (name, stoerung) in Szenen())
        {
            var ein = Mischen(stoerung);
            var teile = Enum.GetValues<Filter>().Select(f => Zeile(f, Bewerten(ein, Filtern(f, ein).Aus, Verzug(f))));
            Console.WriteLine($"  MESSUNG KlangB Qualität {name}: " + string.Join(" | ", teile));
        }
        var probe = Mischen(Szenen()[1].Stoerung);
        foreach (var f in new[] { Filter.Rnnoise, Filter.DeepFilter, Filter.DeepFilterVoll })
        {
            var (aus, _) = Filtern(f, probe);
            var (verzug, k) = AnbindungSignale.BesterVerzug(probe, aus, Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 4800);
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangB Verzögerung {f}: gemessen {verzug} Samples ({verzug / 48.0:0.0} ms, Korrelation {k:0.000}), algorithmisch {Verzug(f)}"));
        }
    }

    /// <summary>Wovon die Treue der Sprache abhängt: der ruhige Korpus 0, +10 und +20 dB lauter, je RNNoise gegen DeepFilterNet3.</summary>
    [Test]
    public static void Messung_KlangB_Pegelabhaengigkeit()
    {
        if (Aus()) return;
        foreach (var db in new[] { 0f, 10f, 20f })
        {
            var ein = Mischen(new float[Klangkorpus.Signal.Length], db);
            var teile = new[] { Filter.Rnnoise, Filter.DeepFilter, Filter.DeepFilterVoll }.Select(f => Zeile(f, Bewerten(ein, Filtern(f, ein).Aus, Verzug(f), db)));
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangB Pegel ruhig {db:+0} dB: ") + string.Join(" | ", teile));
        }
    }

    /// <summary>Zeitbezug Mikrofon zum Gate je Rauschfilter in Samples (APM mit AEC3 plus Verzögerung des Filters).</summary>
    internal static int VerzugGate(Rauschfilter f) => Pumpmessung.VerzugEcho + (Rauschfilterwahl.VerzugRahmen(f) * R);

    /// <summary>Vorlauf von 1,5 s: Grundrauschen und das Ende des Störgeräuschs (Stark rechnet sich darin warm und wird eingeblendet).</summary>
    internal static float[] Vorlauf(float[] stoerung)
    {
        var v = AnbindungSignale.Rauschen(150 * R, Pumpmessung.GrundrauschenDbfs, 78);
        for (var i = 0; i < v.Length; i++) v[i] += stoerung[stoerung.Length - v.Length + i];
        return v;
    }

    /// <summary>
    /// Die echte Kette (Prüfstand, Sprachaktivierung automatisch, Halteregel ab Werk) mit einem Rauschfilter: erst der Vorlauf, dann
    /// der Korpus mit Störgeräusch; Telemetrie ab dem Korpus.
    /// </summary>
    internal static async Task<Pumplauf> KetteLaufen(Rauschfilter f, float[] eingang, float[] vorlauf)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Rauschfilter = f;
        await s.StartenAsync();
        s.Abspielen(vorlauf);
        if (s.Maschine.Rauschfilterstand.Wirksam != f) throw new InvalidOperationException($"{f} wirkt nach dem Vorlauf nicht ({s.Maschine.Rauschfilterstand})");
        s.Telemetrie.Clear();
        s.Pakete.Clear();
        s.Abspielen(eingang);
        return new Pumplauf(Variante.Halteregel() with { Name = f.ToString() }, 0f, eingang, [.. s.Telemetrie], [.. s.Pakete], null, s.Maschine.Telemetrie);
    }

    /// <summary>
    /// Die Rauschfilter in der echten Kette (Sprachaktivierung automatisch, Gate, Halteregel, Opus) je Störgeräusch: abgeschnittene
    /// Wortanfänge (erste 30 ms) und Satzenden (letzte 150 ms) am Gate, Fehlauslösungen (gesendet ohne Satz), Sprachpegel und Rest in
    /// den Pausen am Gate, dazu die Laufzeit der ganzen Kette je Rahmen und die verpassten Fristen.
    /// </summary>
    [Test]
    public static async Task Messung_KlangB_Kette()
    {
        if (Aus()) return;
        var szenen = Szenen();
        var laeufe = new List<(int Szene, Rauschfilter Filter, Task<Pumplauf> Lauf)>();
        using var plaetze = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 4));
        for (var i = 0; i < szenen.Length; i++)
        {
            var eingang = Pumpmessung.Eingang(0f);
            var stoerung = szenen[i].Stoerung;
            for (var k = 0; k < eingang.Length; k++) eingang[k] += stoerung[k];
            var vorlauf = Vorlauf(stoerung);
            foreach (var f in new[] { Rauschfilter.Standard, Rauschfilter.Stark, Rauschfilter.Aus })
            {
                laeufe.Add((i, f, Task.Run(async () =>
                {
                    await plaetze.WaitAsync();
                    try { return await KetteLaufen(f, eingang, vorlauf); }
                    finally { plaetze.Release(); }
                })));
            }
        }
        await Task.WhenAll(laeufe.Select(l => l.Lauf));
        for (var i = 0; i < szenen.Length; i++)
        {
            var teile = laeufe.Where(l => l.Szene == i).Select(l =>
            {
                var lauf = l.Lauf.Result;
                var verzug = VerzugGate(l.Filter);
                var k = Pumpmessung.Auswerten(lauf, verzug);
                var t = lauf.Telemetrie;
                var rest = Enumerable.Range(0, t.Count).Where(fr => !Klangkorpus.Aeusserungen.Any(a => fr * R < a.Ende + verzug + 14400 && (fr + 1) * R > a.Start + verzug - 14400))
                    .Select(fr => Math.Pow(10, t[fr].GatePegelDbfs / 10.0)).DefaultIfEmpty(1e-30).Average();
                var st = lauf.Stand.Stufe(Funk.Klang.Maschine.KlangStufe.Gesamt);
                return string.Create(C,
                    $"{l.Filter}: Anfänge ab {k.AnfaengeAbgeschnitten}/{k.Woerter}, Enden ab {k.EndenAbgeschnitten}/{k.Aeusserungen}, Fehl {k.Fehlausloesungen}, Sprache am Gate {k.SprachePegelDbfs:0.0} dBFS, Pausen am Gate {10 * Math.Log10(rest):0.0} dBFS, Kette p50/p99/max {st.P50Us:0}/{st.P99Us:0}/{st.MaxUs:0} µs, Fristen {lauf.Stand.DeadlineVerpasst}");
            });
            Console.WriteLine($"  MESSUNG KlangB Kette {szenen[i].Name}: " + string.Join(" | ", teile));
        }
    }

    /// <summary>
    /// Hörproben für Sarevocs Blindvergleich, nur mit FUNK_HOERPROBEN (Ordner unter C:\Claude): je Störgeräusch der erste Block des Korpus
    /// (Stefan, sieben Sätze) mit gehaltener Sprechtaste durch die echte Kette mit Standard und mit Stark, gesendet und von einer
    /// zweiten Klangmaschine empfangen (Jitterpuffer, Mischer, ohne Klicks). Je Szene ein Paar A/B in zufälliger Zuordnung, die nur in
    /// Aufloesung\Zuordnung.txt steht, dazu der Eingang. Die Laufzeit Eingang bis Empfang (Kreuzkorrelation) zeigt die 10 ms mehr von
    /// Stark.
    /// </summary>
    [Test]
    public static async Task Messung_KlangB_Hoerproben()
    {
        if (Environment.GetEnvironmentVariable("FUNK_HOERPROBEN") is not { Length: > 0 } ordner)
        {
            Console.WriteLine("HINWEIS Hörproben Klangstufe B übersprungen (FUNK_HOERPROBEN setzen).");
            return;
        }
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_HOERPROBEN muss unter C:\Claude liegen: " + ziel);
        Directory.CreateDirectory(Path.Combine(ziel, "Aufloesung"));
        var zuordnung = new List<string> { "Zuordnung der Hörproben Klangstufe B (erst nach dem Hören öffnen)", "" };
        var ende = Klangkorpus.Aeusserungen[7].Start / R * R;
        var szenen = Szenen();
        foreach (var (nr, name) in new[] { (0, "1"), (1, "2"), (3, "3"), (4, "4"), (5, "5"), (6, "6") })
        {
            var eingang = Pumpmessung.Eingang(0f);
            var stoerung = szenen[nr].Stoerung;
            for (var k = 0; k < eingang.Length; k++) eingang[k] += stoerung[k];
            var stueck = eingang[..ende];
            var vorlauf = Vorlauf(stoerung);
            MessungKlangA.WavSchreiben(Path.Combine(ziel, $"Paar{name}-Eingang.wav"), stueck);
            var standard = await MitTasteEmpfangen(Rauschfilter.Standard, stueck, vorlauf);
            var stark = await MitTasteEmpfangen(Rauschfilter.Stark, stueck, vorlauf);
            var tausch = Random.Shared.Next(2) == 1;
            MessungKlangA.WavSchreiben(Path.Combine(ziel, $"Paar{name}-A.wav"), tausch ? stark : standard);
            MessungKlangA.WavSchreiben(Path.Combine(ziel, $"Paar{name}-B.wav"), tausch ? standard : stark);
            var gesamt = vorlauf.Concat(stueck).ToArray();
            int Verzug(float[] aus) => AnbindungSignale.BesterVerzug(gesamt, aus, vorlauf.Length + Klangkorpus.Aeusserungen[0].Start, 2 * 48000, 12000).Verzug;
            zuordnung.Add(string.Create(C, $"Paar {name} ({szenen[nr].Name}): A = {(tausch ? "Stark" : "Standard")}, B = {(tausch ? "Standard" : "Stark")}"));
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangB Hörprobe Paar {name} ({szenen[nr].Name}): Laufzeit Eingang bis Empfang Standard {Verzug(standard)}, Stark {Verzug(stark)} Samples"));
        }
        zuordnung.Add("");
        zuordnung.Add("Standard = RNNoise (ab Werk), Stark = DeepFilterNet3. Beide mit derselben Kette (AEC3 und Hochpass, Halteregel, Opus 48 kbit/s),");
        zuordnung.Add("Sprechtaste gehalten, also ohne Gate: Man hört den Filter auch in den Pausen. Eingang = was das Mikrofon lieferte.");
        zuordnung.Add("A und B sind so laut wie ein Kanal beim Empfänger (3 dB unter mittig gehört), damit keine Spitze an 0 dBFS stößt.");
        await File.WriteAllLinesAsync(Path.Combine(ziel, "Aufloesung", "Zuordnung.txt"), zuordnung);
    }

    /// <summary>Vorlauf, dann <paramref name="stueck"/> mit gehaltener Sprechtaste durch die Kette, von einer zweiten Maschine empfangen.</summary>
    private static async Task<float[]> MitTasteEmpfangen(Rauschfilter f, float[] stueck, float[] vorlauf)
    {
        await using var sender = new Pruefstand();
        sender.Maschine.Rauschfilter = f;
        sender.Maschine.Sprachaktivierung = false;
        await sender.StartenAsync();
        sender.Abspielen(vorlauf);
        if (sender.Maschine.Rauschfilterstand.Wirksam != f) throw new InvalidOperationException($"{f} wirkt nach dem Vorlauf nicht");
        sender.Maschine.SprechtasteGedrueckt();
        sender.Abspielen(stueck);
        sender.Maschine.SprechtasteLosgelassen();
        sender.Stille(50);
        // Der Empfänger liefert mono wie mittig gehört (+3 dB über einem Kanal); 3 dB zurück, damit keine Spitze an 0 dBFS stößt.
        var mono = await MessungKlangA.Empfaenger(sender.Pakete, (int)sender.Takte + 80);
        for (var i = 0; i < mono.Length; i++) mono[i] *= 0.70710678f;
        return mono;
    }

    /// <summary>
    /// Laufzeit je 10-ms-Rahmen der Filter über den Korpus mit allen Störgeräuschen (110 s), im eigenen Thread mit Normal- oder
    /// höchster Priorität, ohne und mit künstlicher Last (so viele Threads wie Kerne rechnen ohne Pause mit Normalpriorität). Dazu
    /// Anlegezeit und Speicher (privat) von DeepFilterNet3.
    /// </summary>
    [Test]
    public static void Messung_KlangB_Laufzeit()
    {
        if (Aus()) return;
        var ein = Mischen(Szenen()[^1].Stoerung);
        using (var p = Process.GetCurrentProcess())
        {
            GC.Collect();
            p.Refresh();
            var vor = p.PrivateMemorySize64;
            var t = Stopwatch.StartNew();
            using var df = new DeepFilterProzessor();
            var anlegen = t.Elapsed.TotalMilliseconds;
            p.Refresh();
            var nachAnlegen = p.PrivateMemorySize64;
            var aus = new float[R];
            for (var r = 0; r < ein.Length / R; r++) df.Verarbeiten(ein.AsSpan(r * R, R), aus);
            p.Refresh();
            Console.WriteLine(string.Create(C, $"  MESSUNG KlangB Speicher DeepFilterNet3: Anlegen {anlegen:0} ms, privat +{(nachAnlegen - vor) / 1048576.0:0.0} MB, nach {ein.Length / R} Rahmen +{(p.PrivateMemorySize64 - vor) / 1048576.0:0.0} MB"));
        }
        var faelle = new (string Name, int Last, ThreadPriority Prio)[]
        {
            ("ohne Last", 0, ThreadPriority.Normal), ("Last, Filter normal", Environment.ProcessorCount, ThreadPriority.Normal),
            ("Last, Filter höchste", Environment.ProcessorCount, ThreadPriority.Highest),
        };
        foreach (var (name, last, prio) in faelle)
        {
            foreach (var f in new[] { Filter.Rnnoise, Filter.DeepFilter, Filter.DeepFilterVoll })
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
                double[] zeiten = [];
                var lauf = new Thread(() => zeiten = Filtern(f, ein).ZeitUs) { Priority = prio };
                lauf.Start();
                lauf.Join();
                stopp.Cancel();
                foreach (var t in lastThreads) t.Join();
                Console.WriteLine($"  MESSUNG KlangB Laufzeit {name} ({last} Lastthreads), {f}: erster Rahmen {zeiten[0].ToString("0", C)} µs, " + Verteilung(zeiten));
            }
        }
    }
}
