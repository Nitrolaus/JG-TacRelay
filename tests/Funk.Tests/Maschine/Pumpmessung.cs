// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>Eine Sendekette für den Vergleich: Pegelregelung im APM (AGC2, alte Kette) oder dahinter (Pegelstufe).</summary>
/// <param name="Schatten">
/// AGC2 läuft im APM: Ein zweites APM ohne AGC2 bekommt dieselben Rahmen; der Pegelunterschied beider Ausgänge ist die
/// Verstärkung der AGC2 (AEC3 und Hochpass rechnen in beiden bitgleich).
/// </param>
/// <param name="HinterRnnoise">Pegelstufe und Begrenzer hinter RNNoise (<see cref="Sendekette.PegelstufeHinterRnnoise"/>, nur Vergleich).</param>
internal sealed record Variante(string Name, bool MitPegelstufe, ApmEinstellungen Apm, LautstaerkeEinstellung Lautstaerke, bool Schatten, bool HinterRnnoise = false)
{
    /// <summary>Die Kette bis 0.6.0 Stufe 2c: AEC3, Hochpass und AGC2 (adaptiv, höchstens 30 dB) im APM, keine Pegelstufe.</summary>
    public static Variante Alt { get; } = new("alt: AGC2 im APM, 30 dB", false, ApmEinstellungen.Sprechfunk, default, true);

    public static Variante Agc2(float maxDb) =>
        new(string.Create(CultureInfo.InvariantCulture, $"AGC2 im APM, {maxDb:0} dB"), false, ApmEinstellungen.Sprechfunk with { Agc2MaxGainDb = maxDb }, default, true);

    /// <summary>Neue Kette mit Halteregel ab dem Startwert; <paramref name="gelernt"/>: setzt dort fort wie nach einer früheren Sitzung.</summary>
    public static Variante Halteregel(float startDb = Pegelstufe.StartDb, bool gelernt = false) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Halteregel {(gelernt ? "gelernt" : "ab")} {startDb:+0;-0} dB"), true, ApmEinstellungen.EchoUndHochpass,
            new LautstaerkeEinstellung(true, startDb, Gelernt: gelernt), false);

    public static Variante Fest(float db) =>
        new(string.Create(CultureInfo.InvariantCulture, $"fest {db:+0.0;-0.0} dB"), true, ApmEinstellungen.EchoUndHochpass, new LautstaerkeEinstellung(false, db), false);
}

/// <summary>Ein Lauf des Klangkorpus durch eine Kette: Telemetrie je Rahmen, gesendete Pakete und (alte Kette) die AGC2.</summary>
internal sealed record Pumplauf(Variante Variante, float MikrofonDb, float[] Eingang, List<TelemetrieEintrag> Telemetrie, List<Gesendet> Pakete,
    float[]? Agc2Db, TelemetrieStand Stand);

/// <summary>Kennzahlen eines Laufs (Recherche Abschnitt 4): Pumpen der Pegelregelung, Abschneidungen, Fehlauslösungen.</summary>
/// <param name="PGainMedian">
/// Median über die Äußerungen von P95(g) - P5(g) der Pegelregelung: AGC2 samt ihrem eingebauten Begrenzer (alte Kette, nicht
/// trennbar) bzw. Pegelstufe (neue Kette; ihr Begrenzer steht in <paramref name="BegrenzerRahmen"/>).
/// </param>
/// <param name="PGainMax">Größter Wert davon.</param>
/// <param name="PGainUeber1">Äußerungen mit mehr als 1 dB.</param>
/// <param name="VGainP95">95-%-Quantil von |dg/dt| innerhalb der Äußerungen in dB/s.</param>
/// <param name="VGainMax">Größter Wert davon.</param>
/// <param name="RegelungMitte">Mittlere Verstärkung der Pegelregelung in den Äußerungen (dB).</param>
/// <param name="RauschfilterSpanne">Median über die Äußerungen von P95 - P5 der Wirkung von RNNoise in aktiven Sprachrahmen (Hintergrund).</param>
/// <param name="AnfaengeAbgeschnitten">Wortanfänge, deren erste 30 ms nicht ganz gesendet wurden.</param>
/// <param name="AnfaengeGedaempft">Wortanfänge, an denen die Pegelregelung mehr als 6 dB unter ihrem Median der Äußerung lag.</param>
/// <param name="EndenAbgeschnitten">Satzenden, deren letzte 150 ms nicht ganz gesendet wurden.</param>
/// <param name="Fehlausloesungen">Sendephasen ohne Äußerung (300 ms Rand).</param>
/// <param name="BegrenzerRahmen">Rahmen, in denen der Begrenzer der neuen Kette um mehr als 0,1 dB absenkte.</param>
/// <param name="BegrenzerTiefsteDb">Stärkste Absenkung des Begrenzers der neuen Kette.</param>
/// <param name="SprachePegelDbfs">Pegel der Sprache am Gate (Mittel über aktive Rahmen der Äußerungen).</param>
/// <param name="SicherAnteil">Anteil der aktiven Rahmen, die RNNoise mit mindestens 0,9 als Sprache erkennt.</param>
internal sealed record Pumpkennzahlen(int Aeusserungen, double PGainMedian, double PGainMax, int PGainUeber1, double VGainP95, double VGainMax,
    double RegelungMitte, double RauschfilterSpanne, int Woerter, int AnfaengeAbgeschnitten, int AnfaengeGedaempft, int EndenAbgeschnitten,
    int Fehlausloesungen, int BegrenzerRahmen, double BegrenzerTiefsteDb, double SprachePegelDbfs, double SicherAnteil, double[] PGainJeAeusserung);

/// <summary>
/// Pumpen reproduzierbar messen (Klangstufe A, Recherche Schritt 2): Der Klangkorpus geht mit einem festen Mikrofonpegel und
/// Grundrauschen von -65 dBFS durch die echte Klangmaschine (Geräte-Attrappen, Headset ohne Echo, Sprachaktivierung
/// automatisch), und je Äußerung wird gemessen, wie stark sich die Verstärkung der Pegelregelung bewegt.
/// <para>
/// Zeitbezug: Das APM mit AEC3 verzögert die Stimme um <see cref="VerzugEcho"/> Samples, RNNoise um weitere 960; ein
/// Mikrofon-Sample s liegt vor RNNoise (AGC2, Pegelstufe) im Rahmen (s + <see cref="VerzugEcho"/>) / 480 und im Gate im Rahmen
/// (s + <see cref="VerzugGate"/>) / 480.
/// </para>
/// </summary>
internal static class Pumpmessung
{
    public const int VerzugEcho = 430;
    public const int VerzugGate = VerzugEcho + RnnoiseProzessor.VerzoegerungSamples;
    public const float GrundrauschenDbfs = -65f;

    /// <summary>Korpus mit <paramref name="mikrofonDb"/> und Grundrauschen, wie ihn das Mikrofon liefert.</summary>
    public static float[] Eingang(float mikrofonDb, ulong seed = 77, float rauschenDbfs = GrundrauschenDbfs)
    {
        var k = Klangkorpus.Signal;
        var x = AnbindungSignale.Rauschen(k.Length, rauschenDbfs, seed);
        var f = Pegel.DbZuLinear(mikrofonDb);
        for (var i = 0; i < x.Length; i++) x[i] += f * k[i];
        return x;
    }

    public static async Task<Pumplauf> Ausfuehren(Variante v, float mikrofonDb, float[]? eingang = null)
    {
        eingang ??= Eingang(mikrofonDb);
        await using var s = new Pruefstand(mitPegelstufe: v.MitPegelstufe, apm: v.Apm) { TelemetrieMitschreiben = true };
        if (v.MitPegelstufe) s.Maschine.Lautstaerke = v.Lautstaerke;
        s.Maschine.Kette.PegelstufeHinterRnnoise = v.HinterRnnoise;
        await s.StartenAsync();
        var takte = eingang.Length / 480;
        float[]? agc = null;
        if (v.Schatten)
        {
            agc = new float[takte];
            using var schatten = new EchoProzessor();
            schatten.Konfigurieren(v.Apm with { Agc2Adaptiv = false });
            _ = schatten.SetzeVerzoegerung(new KlangMaschinenOptionen().EchoVerzoegerungMs);
            var stille = new float[480];
            var kopie = new float[480];
            for (var t = 0; t < takte; t++)
            {
                s.Takt(eingang.AsSpan(t * 480, 480));
                _ = schatten.VerarbeiteWiedergabe(stille);
                eingang.AsSpan(t * 480, 480).CopyTo(kopie);
                _ = schatten.VerarbeiteAufnahme(kopie);
                var ohne = Pegel.Dbfs(kopie);
                agc[t] = ohne > -90f ? s.Telemetrie[t].EchoDbfs - ohne : float.NaN;
            }
        }
        else
        {
            s.Abspielen(eingang);
        }
        return new Pumplauf(v, mikrofonDb, eingang, [.. s.Telemetrie], [.. s.Pakete], agc, s.Maschine.Telemetrie);
    }

    /// <summary>
    /// Welche Rahmen gesendet wurden, nachgerechnet aus dem Zustand des Gates: beim Öffnen der Vorlauf vor dem Anlauf (bis Klangstufe C1
    /// 30 ms, seitdem ab Werk <see cref="GateVorgaben.VorlaufAbWerkMs"/>), höchstens <see cref="GateEinstellungen.BurstHoechstRahmen"/>
    /// vergangene Rahmen, nie vor dem zuletzt gesendeten (wie <c>SprachGate.Oeffnen</c>). Öffnet das Gate direkt aus Leise (Funkniveau während der
    /// Raum-Anhebung, Klangstufe C2a), geht der ganze Vorpuffer mit (Burst-Grenze). Ohne Sprechtaste: die öffnet auch direkt, aber ohne Vorlauf.
    /// </summary>
    public static bool[] GesendeteRahmen(List<TelemetrieEintrag> t, int vorlaufRahmen = GateVorgaben.VorlaufAbWerkMs / 10)
    {
        var burst = new GateEinstellungen().BurstHoechstRahmen;
        var gesendet = new bool[t.Count];
        var letzter = -1;
        var anlauf = -1;
        var zuvor = GateZustand.Leise;
        for (var f = 0; f < t.Count; f++)
        {
            var z = t[f].Gate;
            if (z == GateZustand.Anlauf && anlauf < 0) anlauf = f;
            if (z is GateZustand.Senden or GateZustand.Nachlauf)
            {
                if (anlauf >= 0)
                {
                    for (var r = Math.Max(Math.Max(letzter + 1, anlauf - vorlaufRahmen), f - burst); r < f; r++) gesendet[r] = true;
                }
                else if (zuvor == GateZustand.Leise)
                {
                    for (var r = Math.Max(letzter + 1, f - burst); r < f; r++) gesendet[r] = true;
                }
                gesendet[f] = true;
                letzter = f;
            }
            zuvor = z;
            // Zurückgehalten (Klangstufe C1): der Anlauf zählt weiter, bis die Phase öffnet oder verfällt.
            if (z is not (GateZustand.Anlauf or GateZustand.Zurueckgehalten)) anlauf = -1;
        }
        return gesendet;
    }

    /// <param name="verzugGate">Zeitbezug Mikrofon zum Gate in Samples; Rauschfilter Stark: <see cref="VerzugEcho"/> + 1440, Aus: + 0.</param>
    public static Pumpkennzahlen Auswerten(Pumplauf l, int verzugGate = VerzugGate, int vorlaufRahmen = GateVorgaben.VorlaufAbWerkMs / 10)
    {
        var t = l.Telemetrie;
        var n = t.Count;
        // Pegelregelung je Rahmen auf der Zeitachse vor RNNoise (dort sitzen AGC2 und Pegelstufe): AGC2 oder Pegelstufe.
        var regelung = new double[n];
        for (var f = 0; f < n; f++)
        {
            var agc = l.Agc2Db is { } a ? (float.IsFinite(a[f]) ? a[f] : float.NaN) : 0f;
            regelung[f] = agc + t[f].PegelstufeDb;
        }
        var gesendet = GesendeteRahmen(t, vorlaufRahmen);
        var pGain = new List<double>();
        var vGain = new List<double>();
        var mitte = new List<double>();
        var rauschfilter = new List<double>();
        var pegel = new List<double>();
        int aktivGesamt = 0, sicher = 0;
        int woerter = 0, abgeschnitten = 0, gedaempft = 0, enden = 0;
        foreach (var a in Klangkorpus.Aeusserungen)
        {
            var rVon = (a.Start + VerzugEcho) / 480;
            var rBis = Math.Min(n, (a.Ende + VerzugEcho) / 480 + 1);
            var g = new List<double>();
            for (var f = rVon; f < rBis; f++)
            {
                if (double.IsFinite(regelung[f])) g.Add(regelung[f]);
                if (f > rVon && double.IsFinite(regelung[f]) && double.IsFinite(regelung[f - 1])) vGain.Add(Math.Abs(regelung[f] - regelung[f - 1]) * 100);
            }
            var von = (a.Start + verzugGate) / 480;
            var bis = Math.Min(n, (a.Ende + verzugGate) / 480 + 1);
            var sortiert = g.Order().ToArray();
            pGain.Add(Quantil(sortiert, 0.95) - Quantil(sortiert, 0.05));
            var median = Quantil(sortiert, 0.5);
            mitte.Add(g.Average());
            var spitze = Enumerable.Range(von, bis - von).Max(f => t[f].GatePegelDbfs);
            var aktiv = Enumerable.Range(von, bis - von).Where(f => t[f].GatePegelDbfs > spitze - 25f).ToList();
            var r = aktiv.Select(f => (double)t[f].RauschfilterDb).Order().ToArray();
            rauschfilter.Add(Quantil(r, 0.95) - Quantil(r, 0.05));
            pegel.Add(10 * Math.Log10(aktiv.Average(f => Math.Pow(10, t[f].GatePegelDbfs / 10.0))));
            aktivGesamt += aktiv.Count;
            sicher += aktiv.Count(f => t[f].Sprachwahrscheinlichkeit >= 0.9f);
            foreach (var w in a.Woerter)
            {
                woerter++;
                var f0 = (w + verzugGate) / 480;
                var f1 = (w + verzugGate + 1440 - 1) / 480;
                if (Enumerable.Range(f0, f1 - f0 + 1).Any(f => f >= n || !gesendet[f])) abgeschnitten++;
                var r0 = (w + VerzugEcho) / 480;
                var r1 = (w + VerzugEcho + 1440 - 1) / 480;
                if (Enumerable.Range(r0, r1 - r0 + 1).Any(f => f < n && regelung[f] < median - 6)) gedaempft++;
            }
            var e0 = (a.Ende + verzugGate - 7200) / 480;
            var e1 = (a.Ende + verzugGate - 1) / 480;
            if (Enumerable.Range(e0, e1 - e0 + 1).Any(f => f >= n || !gesendet[f])) enden++;
        }
        // Fehlauslösungen: zusammenhängende gesendete Abschnitte ohne Äußerung (300 ms Rand).
        var fehl = 0;
        for (var f = 0; f < n; f++)
        {
            if (!gesendet[f] || (f > 0 && gesendet[f - 1])) continue;
            var ende = f;
            while (ende + 1 < n && gesendet[ende + 1]) ende++;
            var ueberlappt = Klangkorpus.Aeusserungen.Any(a =>
                f * 480 < a.Ende + verzugGate + 14400 && (ende + 1) * 480 > a.Start + verzugGate - 14400);
            if (!ueberlappt) fehl++;
        }
        var pg = pGain.Order().ToArray();
        var vg = vGain.Order().ToArray();
        return new Pumpkennzahlen(pGain.Count, Quantil(pg, 0.5), pg[^1], pGain.Count(x => x > 1), vg.Length == 0 ? 0 : Quantil(vg, 0.95),
            vg.Length == 0 ? 0 : vg[^1], mitte.Average(), Quantil([.. rauschfilter.Order()], 0.5), woerter, abgeschnitten, gedaempft, enden, fehl,
            t.Count(e => e.BegrenzerDb < -0.1f), t.Min(e => e.BegrenzerDb), 10 * Math.Log10(pegel.Average(p => Math.Pow(10, p / 10))), (double)sicher / aktivGesamt, [.. pGain]);
    }

    private static double Quantil(double[] sortiert, double q)
    {
        if (sortiert.Length == 0) return double.NaN;
        var pos = q * (sortiert.Length - 1);
        var u = (int)Math.Floor(pos);
        var o = Math.Min(sortiert.Length - 1, u + 1);
        return sortiert[u] + (sortiert[o] - sortiert[u]) * (pos - u);
    }

    public static string Zeile(Pumplauf l, Pumpkennzahlen k)
    {
        var st = l.Stand;
        string Lz(KlangStufe s) => string.Create(CultureInfo.InvariantCulture, $"{st.Stufe(s).P50Us:0}/{st.Stufe(s).P99Us:0}/{st.Stufe(s).MaxUs:0}");
        return string.Create(CultureInfo.InvariantCulture,
            $"{l.Variante.Name,-26} Mikro {l.MikrofonDb,4:+0;-0} dB | P_Gain Median {k.PGainMedian:0.00} max {k.PGainMax:0.00} dB, >1 dB {k.PGainUeber1}/{k.Aeusserungen} | V_Gain p95 {k.VGainP95:0.0} max {k.VGainMax:0.0} dB/s | Regelung {k.RegelungMitte:+0.0;-0.0} dB, Sprache am Gate {k.SprachePegelDbfs:0.0} dBFS, sicher {k.SicherAnteil:P0} | RNNoise-Spanne {k.RauschfilterSpanne:0.0} dB | Anfänge ab {k.AnfaengeAbgeschnitten}/{k.Woerter} gedämpft {k.AnfaengeGedaempft} | Enden ab {k.EndenAbgeschnitten}/{k.Aeusserungen} | Fehl {k.Fehlausloesungen} | Begrenzer {k.BegrenzerRahmen} Rahmen, tiefste {k.BegrenzerTiefsteDb:0.0} dB | Laufzeit p50/p99/max µs Echo {Lz(KlangStufe.Echo)} RNNoise {Lz(KlangStufe.Rauschfilter)} Pegel {Lz(KlangStufe.Pegelstufe)} Begr {Lz(KlangStufe.Begrenzer)} Gate {Lz(KlangStufe.Gate)} Opus {Lz(KlangStufe.Opus)} Gesamt {Lz(KlangStufe.Gesamt)} | Fristen {st.DeadlineVerpasst}");
    }
}
