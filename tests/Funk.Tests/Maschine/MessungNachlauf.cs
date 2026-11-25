// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Nachlauf der Sprechtaste (Q66, 0.9.3), nur von Hand: FUNK_MESSUNG=1. Wie viel vom Ende eines Worts fehlt, wenn die Sprechtaste genau am
/// Wortende losgelassen wird? Die echte Kette im Prüfstand (Geräte-Attrappen, simulierte Uhr, Sprachaktivierung aus): Taste 200 ms vor dem
/// Wortanfang drücken, genau an der Marke des Wortendes loslassen (sie liegt auf einer Rahmengrenze), die gesendete Phase dekodieren. Bezug
/// ist derselbe Lauf mit der Taste bis 400 ms nach dem Wortende; bis zum Loslassen sind beide Läufe gleich. Verloren heißt: ein 5-ms-Block,
/// der im Bezug Sprache trägt (höchstens 45 dB unter dem lautesten Block der Phase), im Lauf aber mehr als 6 dB leiser ist oder fehlt.
/// Bewertet wird bis 150 ms nach dem Loslassen. Je Rauschfilter (Aus, Standard, Stark), mit und ohne Echounterdrückung und je Nachlauf.
/// <para>
/// Korpus: der Kurzkorpus (54 Ein-Wort-Rufe, Ende = Wortende) und die Satzenden des Klangkorpus, deren nächste Äußerung mindestens 800 ms
/// später beginnt; beide mit Grundrauschen -65 dBFS wie <see cref="Pumpmessung.Eingang"/>.
/// </para>
/// <para>
/// Die Kette misst echte Rechenzeit: Unter Last fällt Stark auf Standard zurück. Deshalb laufen die Läufe mit Stark mit höchstens
/// <see cref="StarkPlaetze"/> zugleich, ein Rückfall wird bis zu <see cref="Versuche"/>-mal wiederholt, und eine Äußerung, die dann immer noch
/// zurückfällt, zählt als "nicht messbar" und fehlt in ihrer Zeile, statt die ganze Messung abzubrechen (Prüfung 0.9.3).
/// </para>
/// </summary>
internal static class MessungNachlauf
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const int R = 480;
    private const int Block = 240;
    private const int VorTasteMs = 200;
    private const int BezugMs = 400;
    private const int FensterMs = 150;

    /// <summary>Nachlaufzeiten der Messung; 0 ist das Verhalten bis 0.9.2, 100 ms die Werkseinstellung ab 0.9.3.</summary>
    internal static readonly int[] Nachlaeufe = [0, 30, 50, 100];

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Nachlauf der Sprechtaste übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    /// <summary>Ergebnis einer Äußerung: verlorene Sprache in ms und wie lange nach dem Loslassen im Bezug noch Sprache aus der Kette kommt.</summary>
    internal readonly record struct Verlust(int VerlorenMs, int SpracheBisMs);

    /// <summary>
    /// Ein Lauf: Taste vor dem Wortanfang drücken, <paramref name="nachEndeRahmen"/> Rahmen nach dem Wortende loslassen; die gesendete Phase
    /// dekodiert. Rückgabe auch der Abstand Druck bis Loslassen in Samples (Lage des Loslassens in der dekodierten Phase).
    /// </summary>
    internal static async Task<(float[] Phase, int Loslassen)> Lauf(Rauschfilter filter, bool echo, float[] signal, KorpusAeusserung a, int nachEndeRahmen,
        int nachlaufMs)
    {
        await using var s = new Pruefstand(klickStartwert: 5);
        s.Maschine.Echounterdrueckung = echo;
        s.Maschine.Sprachaktivierung = false;
        s.Maschine.Rauschfilter = filter;
        s.Maschine.TastenNachlaufMs = nachlaufMs;
        await s.StartenAsync();
        s.Abspielen(AnbindungSignale.Rauschen(150 * R, Pumpmessung.GrundrauschenDbfs, 78));
        if (s.Maschine.Rauschfilterstand.Wirksam != filter)
            throw new InvalidOperationException($"{filter} wirkt nach dem Vorlauf nicht ({s.Maschine.Rauschfilterstand})");
        s.Pakete.Clear();
        // Das Wortende liegt auf einer Rahmengrenze: Abgespielt wird ab einem ganzzahligen Vielfachen von 480 Samples davor.
        var vorher = (a.Ende - a.Start + ((VorTasteMs + 300) * 48)) / R + 1;
        var von = a.Ende - (vorher * R);
        var druck = (a.Start - (VorTasteMs * 48) - von) / R;
        var los = vorher + nachEndeRahmen;
        var takte = vorher + ((BezugMs + 300) / 10);
        for (var k = 0; k < takte; k++)
        {
            if (k == druck) s.Maschine.SprechtasteGedrueckt();
            if (k == los) s.Maschine.SprechtasteLosgelassen();
            s.Takt(signal.AsSpan(von + (k * R), R));
        }
        // Unter Last kann Stark wegen Rechenzeit auf Standard zurückfallen (die Kette misst echte Zeit); dann wäre der Lauf mit dem Bezug
        // nicht mehr vergleichbar. Der Aufrufer wiederholt ihn.
        if (s.Maschine.Rauschfilterstand is not { Rueckfall: null } stand || stand.Wirksam != filter) throw new RueckfallAusnahme();
        return (Phase(s.Pakete), (los - druck) * R);
    }

    private sealed class RueckfallAusnahme : Exception;

    /// <summary>Läufe mit Stark zugleich; mit 16 zugleich fiel Stark bei der Prüfung 0.9.3 dreimal hintereinander zurück, und die Messung brach ab.</summary>
    private const int StarkPlaetze = 4;

    /// <summary>So oft wird ein Lauf nach einem Rückfall von Stark versucht.</summary>
    private const int Versuche = 5;

    /// <summary>Ein Lauf, bei einem Rückfall von Stark bis zu <see cref="Versuche"/>-mal versucht; danach <c>null</c> (nicht messbar).</summary>
    private static async Task<(float[] Phase, int Loslassen)?> Wiederholt(Func<Task<(float[], int)>> lauf)
    {
        for (var versuch = 1; versuch <= Versuche; versuch++)
        {
            try
            {
                return await lauf();
            }
            catch (RueckfallAusnahme)
            {
            }
        }
        return null;
    }


    /// <summary>Die erste gesendete Phase, dekodiert wie beim Empfänger (ohne Jitterpuffer).</summary>
    internal static float[] Phase(IReadOnlyList<Gesendet> pakete)
    {
        using var dek = new OpusDekodierer();
        var pcm = new List<float>();
        var rahmen = new float[OpusDekodierer.MaxSamples];
        var drin = false;
        foreach (var p in pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                if (drin) break;
                drin = true;
            }
            if (!drin) continue;
            if (p.Opus.Length > 0) pcm.AddRange(rahmen.AsSpan(0, dek.Dekodieren(p.Opus, rahmen)).ToArray());
            if ((p.Kennzeichen & Sprachkennzeichen.Ende) != 0) break;
        }
        return [.. pcm];
    }

    /// <summary>
    /// Lauf gegen Bezug, beide ab dem Druck; <paramref name="loslassen"/>: Lage des Loslassens im Lauf. Sprache ist im Bezug, was höchstens
    /// 45 dB unter dem lautesten Block liegt und mindestens 6 dB über dem Rauschen, das der Bezug 200 bis 380 ms nach dem Wortende sendet
    /// (ohne Rauschfilter geht das Grundrauschen mit hinaus und zählte sonst als Sprache).
    /// </summary>
    internal static Verlust Bewerten(float[] lauf, float[] bezug, int loslassen)
    {
        var bis = Math.Min(bezug.Length, loslassen + (FensterMs * 48)) / Block;
        var pegel = new double[bis];
        var spitze = double.NegativeInfinity;
        for (var b = 0; b < bis; b++)
        {
            pegel[b] = Pegel.Dbfs(bezug.AsSpan(b * Block, Block));
            spitze = Math.Max(spitze, pegel[b]);
        }
        var rauschVon = loslassen + (200 * 48);
        var rauschen = rauschVon + (180 * 48) <= bezug.Length ? Pegel.Dbfs(bezug.AsSpan(rauschVon, 180 * 48)) : double.NegativeInfinity;
        var schwelle = Math.Max(Math.Max(spitze - 45, rauschen + 6), -75);
        var verloren = 0;
        var letzte = -1;
        for (var b = 0; b < bis; b++)
        {
            if (pegel[b] < schwelle) continue;
            letzte = b;
            var l = (b + 1) * Block <= lauf.Length ? Pegel.Dbfs(lauf.AsSpan(b * Block, Block)) : double.NegativeInfinity;
            if (l < pegel[b] - 6) verloren++;
        }
        return new Verlust(verloren * Block / 48, letzte < 0 ? 0 : (((letzte + 1) * Block) - loslassen) / 48);
    }

    /// <summary>Kurzkorpus mit Grundrauschen.</summary>
    private static float[] KurzEingang()
    {
        var k = Kurzkorpus.Signal;
        var x = AnbindungSignale.Rauschen(k.Length, Pumpmessung.GrundrauschenDbfs, 79);
        for (var i = 0; i < x.Length; i++) x[i] += k[i];
        return x;
    }

    /// <summary>Satzenden des Klangkorpus mit genug Abstand zur nächsten Äußerung (Bezug und Fenster bleiben in der Pause).</summary>
    private static List<KorpusAeusserung> Saetze()
    {
        var alle = Klangkorpus.Aeusserungen;
        var liste = new List<KorpusAeusserung>();
        for (var i = 0; i < alle.Count; i++)
        {
            var naechste = i + 1 < alle.Count ? alle[i + 1].Start : int.MaxValue;
            if (naechste - alle[i].Ende >= 800 * 48 && alle[i].Start >= (VorTasteMs + 400) * 48) liste.Add(alle[i]);
        }
        return liste;
    }

    private static string Verteilung(IReadOnlyList<int> werte)
    {
        var s = werte.Order().ToArray();
        int Q(double q) => s[Math.Clamp((int)Math.Ceiling(q * s.Length) - 1, 0, s.Length - 1)];
        return string.Create(C, $"Mittel {s.Average():0.0} p50 {Q(0.5)} p90 {Q(0.9)} max {s[^1]}");
    }

    /// <summary>
    /// Verlorene Sprache am Wortende je Rauschfilter, Echounterdrückung und Nachlauf; dazu, wie lange nach dem Loslassen im Bezug noch
    /// Sprache aus der Kette kommt (Verzögerung der Kette plus Ausklang).
    /// </summary>
    [Test]
    public static async Task Messung_Nachlauf_Wortende()
    {
        if (Aus()) return;
        var korpora = new (string Name, float[] Signal, IReadOnlyList<KorpusAeusserung> Aeusserungen)[]
        {
            ("Kurzkorpus", KurzEingang(), Kurzkorpus.Aeusserungen),
            ("Satzenden", Pumpmessung.Eingang(0f), Saetze()),
        };
        using var plaetze = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));
        using var starkPlaetze = new SemaphoreSlim(StarkPlaetze);
        foreach (var (name, signal, aeusserungen) in korpora)
        {
            foreach (var echo in new[] { true, false })
            {
                foreach (var filter in new[] { Rauschfilter.Aus, Rauschfilter.Standard, Rauschfilter.Stark })
                {
                    var platz = filter == Rauschfilter.Stark ? starkPlaetze : plaetze;
                    async Task<(float[] Phase, int Loslassen)?> Begrenzt(Func<Task<(float[], int)>> lauf)
                    {
                        await platz.WaitAsync();
                        try { return await Wiederholt(lauf); }
                        finally { platz.Release(); }
                    }
                    var bezuege = aeusserungen.Select(a => Task.Run(() => Begrenzt(() => Lauf(filter, echo, signal, a, BezugMs / 10, 0)))).ToArray();
                    foreach (var nachlauf in Nachlaeufe)
                    {
                        var laeufe = aeusserungen.Select(a => Task.Run(() => Begrenzt(() => Lauf(filter, echo, signal, a, 0, nachlauf)))).ToArray();
                        await Task.WhenAll(laeufe.Concat(bezuege));
                        var verluste = laeufe.Select((l, i) => l.Result is { } lauf && bezuege[i].Result is { } bezug
                            ? Bewerten(lauf.Phase, bezug.Phase, lauf.Loslassen) : (Verlust?)null).ToList();
                        var gemessen = verluste.OfType<Verlust>().ToList();
                        var zeile = $"  MESSUNG Nachlauf {name}, Echounterdrückung {(echo ? "an" : "aus")}, {filter}, Nachlauf {nachlauf} ms: ";
                        var nichtMessbar = verluste.Count - gemessen.Count;
                        if (gemessen.Count == 0)
                        {
                            Console.WriteLine(zeile + $"nicht messbar ({nichtMessbar} von {verluste.Count}, Rückfall von Stark)");
                            continue;
                        }
                        var mit = gemessen.Count(v => v.VerlorenMs > 0);
                        Console.WriteLine(string.Create(C,
                            $"{zeile}verloren ms {Verteilung([.. gemessen.Select(v => v.VerlorenMs)])}, mit Verlust {mit}/{gemessen.Count}; Sprache bis nach dem Loslassen ms {Verteilung([.. gemessen.Select(v => v.SpracheBisMs)])}{(nichtMessbar > 0 ? $"; nicht messbar {nichtMessbar}" : "")}"));
                    }
                }
            }
        }
    }
}
