// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Messungen aus der Gegenprüfung 0.6.0 Stufe 2b, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben Zahlen für die Abwägung
/// im Echoschutz (Kopplung nur bei laufender Wiedergabe gelernt) und zum Rausch-Klick aus. Nur über Schnittstellen, die es bis Stufe 2
/// schon gab, damit derselbe Text auf älteren Ständen übersetzt und vorher und nachher vergleichbar bleibt.
/// <para>
/// <see cref="Messung_AntwortNachFunk"/> und <see cref="Messung_InsWortFallen"/>: Ein anderer Funker spricht einzelne Sprüche mit der
/// Taste (je eine Phase mit Beginn und Ende, also mit beiden Funk-Klicks), die Wiedergabe kommt über den Echopfad 40 ms später ins
/// Mikrofon, und der Nutzer antwortet je Spruch einmal, <c>abstand</c> ms nach dessen Ende beim Sender (negativ: er fällt ins Wort).
/// Gezählt: verpasste Anlaute (keine Phase, die spätestens 100 ms nach dem Anlaut beginnt), die ersten drei getrennt (Kopplung noch
/// nicht gelernt), mittlerer Verzug ab Anlaut, Phasen ohne eigene Rede, gelernte Kopplung am Ende. Gemessen am 02.10.2026 über je
/// drei Seeds (verpasst von 1128 Antworten bzw. 564 Anlauten beim Ins-Wort-Fallen): main 755d868 269 und 350, Stufe 2b 47466cb 254
/// und 370, Stufe 2b mit dem alten Lernen (auch in der Haltezeit) 237 und 360, dort aber eine Fehlphase im KlickMaschineTests.
/// </para>
/// <para>
/// Zum Klick: Spitzen allein (ganz laut, mono), Summe mit lauter Sprache vor dem Begrenzer, Rechenzeit eines Mischer-Blocks, in dem
/// Klicks starten (seit 2b entsteht das Rauschen dabei im Audio-Thread), und die eigene Taste über Lautsprecher bei laufender
/// Sprachaktivierung (öffnet das Echo des Loslass-Klicks das Gate?).
/// </para>
/// </summary>
internal static class MessungAntwortUndKlick
{
    internal static readonly (string Name, Func<float, float>? Pfad)[] Pfade =
    [
        ("kein Echo", null), ("3 dB", KlangMaschineTests.Kopplung3Db), ("12 dB", KlangMaschineTests.Kopplung12Db),
        ("20 dB", x => 0.1f * x), ("30 dB", x => 0.0316f * x), ("verzerrt", KlangMaschineTests.KopplungVerzerrt),
    ];

    private static readonly int[] Seeds = [151, 171, 181];

    internal readonly record struct Ergebnis(int Verpasst, int Gesamt, int FruehVerpasst, int FruehGesamt, double VerzugSumme, int VerzugAnzahl,
        int Fehl, float Kopplung);

    private static bool Aus()
    {
        if (Environment.GetEnvironmentVariable("FUNK_MESSUNG") == "1") return false;
        Console.WriteLine("HINWEIS Messung Antwort und Klick übersprungen (FUNK_MESSUNG=1 setzen).");
        return true;
    }

    [Test]
    public static Task Messung_AntwortNachFunk() => Antworten("Antwort", [150, 300, 450, 600, 900]);

    [Test]
    public static Task Messung_InsWortFallen() => Antworten("InsWort", [-600, -300]);

    private static async Task Antworten(string name, int[] abstaende)
    {
        if (Aus()) return;
        var laeufe = new List<(string Pfad, int Abstand, Task<Ergebnis> Lauf)>();
        foreach (var (pfadName, pfad) in Pfade)
        {
            foreach (var abstand in abstaende)
            {
                foreach (var seed in Seeds) laeufe.Add((pfadName, abstand, Task.Run(() => Szene(pfad, abstand, seed))));
            }
        }
        var c = CultureInfo.InvariantCulture;
        int alleV = 0, alleG = 0, alleF = 0;
        foreach (var (pfadName, _) in Pfade)
        {
            var teile = new List<string>();
            foreach (var abstand in abstaende)
            {
                var e = await Task.WhenAll(laeufe.Where(l => l.Pfad == pfadName && l.Abstand == abstand).Select(l => l.Lauf));
                var v = e.Sum(x => x.Verpasst);
                var g = e.Sum(x => x.Gesamt);
                var verzug = e.Sum(x => x.VerzugSumme) / Math.Max(1, e.Sum(x => x.VerzugAnzahl));
                var fehl = e.Sum(x => x.Fehl);
                alleV += v;
                alleG += g;
                alleF += fehl;
                teile.Add(string.Create(c, $"{abstand} ms {v}/{g} (früh {e.Sum(x => x.FruehVerpasst)}/{e.Sum(x => x.FruehGesamt)}) {verzug:F0} ms F{fehl} K{e.Average(x => x.Kopplung):F1}"));
            }
            Console.WriteLine($"  MESSUNG {name} {pfadName}: " + string.Join(" | ", teile));
        }
        Console.WriteLine($"  MESSUNG {name} gesamt: verpasst {alleV}/{alleG}, Fehlphasen {alleF}");
    }

    /// <summary>
    /// Eine Szene zu 60 s: Sprüche des anderen als Phasen, je Spruch eine Antwort <paramref name="abstandMs"/> nach seinem Ende.
    /// <paramref name="stand"/> legt einen anderen Prüfstand an (etwa die alte Kette), sonst der übliche.
    /// </summary>
    internal static async Task<Ergebnis> Szene(Func<float, float>? pfad, int abstandMs, int seed, Func<Pruefstand>? stand = null)
    {
        const int Rate = 48000;
        const double Sekunden = 60;
        var (fern, fernMarken) = Testszenen.Sprache(Sekunden, seed + 1000);
        var (nah, nahMarken) = Testszenen.Sprache(Sekunden, seed);
        var n = nah.Length;
        var mikrofon = AnbindungSignale.Rauschen(n, -65, (ulong)seed);
        var phasen = new List<(int Start, float[] Signal)>();
        var antworten = new List<Aeusserung>();
        var t = Rate;
        for (var k = 0; ; k++)
        {
            var f = fernMarken[k % fernMarken.Count];
            var fa = Math.Max(0, f.Start - (Rate * 15 / 100));
            var fe = Math.Min(fern.Length, f.Ende + (Rate / 20));
            var flen = (fe - fa) / 960 * 960;
            var a = nahMarken[k % nahMarken.Count];
            var na = Math.Max(0, a.Start - (Rate * 15 / 100));
            var ne = Math.Min(nah.Length, a.Ende + (Rate / 20));
            var anlaut = t + flen + (abstandMs * 48);
            var nStart = anlaut - (a.Start - na);
            if (nStart + (ne - na) + (2 * Rate) > n) break;
            var stueck = new float[flen];
            Array.Copy(fern, fa, stueck, 0, flen);
            phasen.Add((t, stueck));
            for (var i = 0; i < ne - na; i++) mikrofon[nStart + i] += nah[na + i];
            antworten.Add(new Aeusserung(anlaut, anlaut + (a.Ende - a.Start)));
            t = (nStart + (ne - na) + (Rate * (15 + (k % 3 * 5)) / 10)) / 960 * 960;
        }

        using var kodierer = new OpusKodierer();
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        var pakete = new List<(long Takt, ushort Phase, uint Frame, Sprachkennzeichen Kennzeichen, byte[] Daten)>();
        for (var p = 0; p < phasen.Count; p++)
        {
            var (start, signal) = phasen[p];
            uint frame = 0;
            for (var i = 0; i + 960 <= signal.Length; i += 960, frame++)
            {
                var m = kodierer.Kodieren(signal.AsSpan(i, 960), puffer);
                pakete.Add(((start + i) / 480 + 3, (ushort)(10 + p), frame, frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer[..m]));
            }
            var endeTakt = ((start + signal.Length) / 480) + 3;
            for (var e = 0; e < 3; e++) pakete.Add((endeTakt, (ushort)(10 + p), frame + (uint)e, Sprachkennzeichen.Ende, []));
        }

        await using var s = stand?.Invoke() ?? new Pruefstand();
        s.Maschine.Echounterdrueckung = true;
        await s.StartenAsync();
        if (pfad is not null) EchoAnlegen(s, pfad);
        var naechstes = 0;
        s.Abspielen(mikrofon, takt =>
        {
            while (naechstes < pakete.Count && pakete[naechstes].Takt <= takt)
            {
                var p = pakete[naechstes++];
                _ = s.Maschine.SpracheEmpfangen(77, 0, p.Phase, p.Frame, p.Kennzeichen, p.Daten, s.Uhr.Us);
            }
        });

        var ph = Phasen(s.Pakete);
        int verpasst = 0, fruehVerpasst = 0, fruehGesamt = 0, verzugAnzahl = 0;
        double verzugSumme = 0;
        for (var k = 0; k < antworten.Count; k++)
        {
            var a = antworten[k];
            var gesendet = ph.Any(x => x.Beginn * 480 <= a.Start + 4800 && (x.Ende + 1) * 480 >= a.Start);
            if (!gesendet) verpasst++;
            if (k < 3)
            {
                fruehGesamt++;
                if (!gesendet) fruehVerpasst++;
            }
            var erste = ph.Where(x => x.Beginn * 480 <= a.Start + 24000 && (x.Ende + 1) * 480 >= a.Start).Select(x => x.Beginn).DefaultIfEmpty(-1).First();
            if (erste >= 0)
            {
                verzugSumme += Math.Max(0, ((erste * 480) - a.Start) / 48.0);
                verzugAnzahl++;
            }
        }
        var fehl = ph.Count(x => !antworten.Any(a => x.Beginn * 480 < a.Ende + 24000 && (x.Ende + 1) * 480 > a.Start - 24000));
        return new Ergebnis(verpasst, antworten.Count, fruehVerpasst, fruehGesamt, verzugSumme, verzugAnzahl, fehl, s.Maschine.Kette.Echoschutz.KopplungDb);
    }

    /// <summary>Spitzen des Klicks allein, ganz laut (-6 dB) und mono (volle Höhe): die höchste und wie oft über dem Knie (-2 dBFS).</summary>
    [Test]
    public static void Messung_KlickSpitzen()
    {
        if (Aus()) return;
        var knie = Pegel.DbZuLinear(-2f);
        foreach (var art in new[] { KlickArt.Druck, KlickArt.Loslassen })
        {
            var k = new FunkKlick(new FunkKlickEinstellungen { EigeneDb = FunkKlickEinstellungen.MaxDb });
            var block = new float[480];
            var hoechste = 0f;
            var ueber = 0;
            const int Anzahl = 100_000;
            for (var r = 0; r < Anzahl; r++)
            {
                k.Anfordern(art);
                var spitze = 0f;
                for (var b = 0; b < 11; b++)
                {
                    Array.Clear(block);
                    k.Mischen(block, Span<float>.Empty);
                    spitze = MathF.Max(spitze, Pegel.Spitze(block));
                }
                hoechste = MathF.Max(hoechste, spitze);
                if (spitze > knie) ueber++;
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  MESSUNG Klick {art} ganz laut mono: höchste Spitze {Pegel.LinearZuDb(hoechste):F2} dBFS in {Anzahl}, über -2 dBFS {ueber}"));
        }
    }

    /// <summary>
    /// Klick neben lauter Sprache im Mischer: Sprache (synthetisch, Plateau -26 bis -12 dBFS, mittig) 60 s, dazu alle 370 ms abwechselnd
    /// Drücken und Loslassen der eigenen Taste. Gezählt: Blöcke, deren Summe vor dem Begrenzer über dem Knie (-2 dBFS) liegt, ohne Klick,
    /// ab Werk (-12 dB) und ganz laut (-6 dB).
    /// </summary>
    [Test]
    public static void Messung_KlickNebenSprache_Begrenzer()
    {
        if (Aus()) return;
        var sprache = Testszenen.Sprache(60, 61).Signal;
        for (var i = 0; i < sprache.Length; i++) sprache[i] *= 2f;
        var knie = Pegel.DbZuLinear(-2f);
        int Zaehlen(FunkKlick? klick)
        {
            var m = new Mischer(new MischerEinstellungen { Kanaele = 2 }) { Klick = klick };
            _ = m.QuelleHinzufuegen(new FeldQuelle(sprache), 0, 0f);
            var aus = new float[960];
            var ueber = 0;
            for (var b = 0; b < sprache.Length / 480; b++)
            {
                if (klick is not null && b % 37 == 0) klick.Anfordern(b / 37 % 2 == 0 ? KlickArt.Druck : KlickArt.Loslassen);
                m.Mischen(aus, 2);
                if (m.SpitzeVorBegrenzer > knie) ueber++;
            }
            return ueber;
        }
        var ohne = Zaehlen(null);
        var werk = Zaehlen(new FunkKlick(new FunkKlickEinstellungen()));
        var laut = Zaehlen(new FunkKlick(new FunkKlickEinstellungen { EigeneDb = FunkKlickEinstellungen.MaxDb }));
        Console.WriteLine($"  MESSUNG Klick neben Sprache, Blöcke über dem Knie in 6000: ohne Klick {ohne}, ab Werk {werk}, ganz laut {laut} (162 Klicks)");
    }

    /// <summary>Rechenzeit eines Mischer-Blocks (10 ms), in dem drei Klicks starten, gegen einen ohne: Median, 99 % und höchster Wert.</summary>
    [Test]
    public static void Messung_KlickRechenzeit()
    {
        if (Aus()) return;
        var k = new FunkKlick(new FunkKlickEinstellungen());
        var m = new Mischer(new MischerEinstellungen { Kanaele = 2 }) { Klick = k };
        var aus = new float[960];
        var mitStart = new List<double>();
        var ohneStart = new List<double>();
        for (var r = 0; r < 3000; r++)
        {
            k.Anfordern(KlickArt.Loslassen);
            k.Anfordern(KlickArt.Druck);
            _ = k.AnhoerenAnfordern();
            var t0 = Stopwatch.GetTimestamp();
            m.Mischen(aus, 2);
            mitStart.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            for (var b = 0; b < 40; b++) m.Mischen(aus, 2);
            var t1 = Stopwatch.GetTimestamp();
            m.Mischen(aus, 2);
            ohneStart.Add(Stopwatch.GetElapsedTime(t1).TotalMilliseconds);
        }
        mitStart.Sort();
        ohneStart.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  MESSUNG Klick Rechenzeit je Block: mit Start Median {mitStart[1500]:F3} ms, 99 % {mitStart[2970]:F3}, höchste {mitStart[^1]:F3}; ohne Start Median {ohneStart[1500]:F3} ms"));
    }

    /// <summary>
    /// Eigene Taste über Lautsprecher bei laufender Sprachaktivierung: je Seed 20-mal drücken, sprechen, loslassen; nach dem Loslassen
    /// spielt der Loslass-Klick über die Lautsprecher. Gezählt: Phasen, die danach ohne eigene Rede beginnen, ab Werk und ganz laut.
    /// </summary>
    [Test]
    public static async Task Messung_EigeneTasteUeberLautsprecher()
    {
        if (Aus()) return;
        var zeilen = new List<string>();
        foreach (var (name, pfad) in Pfade.Where(p => p.Pfad is not null && p.Name is "3 dB" or "12 dB" or "verzerrt"))
        {
            foreach (var db in new[] { FunkKlickEinstellungen.EigeneVoreinstellungDb, FunkKlickEinstellungen.MaxDb })
            {
                var fehl = 0;
                foreach (var seed in new[] { 5, 6, 7 }) fehl += await EigeneTaste(pfad!, db, seed);
                zeilen.Add($"{name} {db} dB: {fehl}");
            }
        }
        Console.WriteLine("  MESSUNG Eigene Taste über Lautsprecher, Phasen nach dem Loslassen in 3 x 20: " + string.Join("; ", zeilen));
    }

    private static async Task<int> EigeneTaste(Func<float, float> pfad, float eigeneDb, int seed)
    {
        await using var s = new Pruefstand();
        s.Maschine.Echounterdrueckung = true;
        s.Maschine.Klick = new FunkKlickEinstellungen { EigeneDb = eigeneDb };
        await s.StartenAsync();
        EchoAnlegen(s, pfad);
        var (sprache, marken) = Testszenen.Sprache(70, seed);
        var rauschen = AnbindungSignale.Rauschen(sprache.Length, -65, (ulong)seed);
        s.Abspielen(rauschen.AsSpan(0, 500 * 480));
        var losgelassen = new List<long>();
        var pos = 500 * 480;
        foreach (var a in marken.Take(20))
        {
            // Drücken, 100 ms später die Äußerung, 200 ms nach ihrem Ende loslassen, dann 2,5 s Stille.
            if (pos + (a.Ende - a.Start) + (48000 * 3) > sprache.Length) break;
            s.Maschine.SprechtasteGedrueckt();
            s.Abspielen(rauschen.AsSpan(pos, 4800));
            var stueck = new float[(a.Ende - a.Start + 9600) / 480 * 480];
            Array.Copy(sprache, a.Start, stueck, 0, Math.Min(stueck.Length, sprache.Length - a.Start));
            s.Abspielen(stueck);
            s.Maschine.SprechtasteLosgelassen();
            losgelassen.Add(s.Takte);
            s.Abspielen(rauschen.AsSpan(pos, 250 * 480));
            pos += 4800;
        }
        var beginn = s.Pakete.Where(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0).Select(p => p.Takt).ToList();
        return beginn.Count(b => losgelassen.Any(l => b > l + 2 && b < l + 250));
    }

    /// <summary>Echo der Wiedergabe (Mittel beider Kanäle) über <paramref name="pfad"/>, 40 ms später im Mikrofon.</summary>
    private static void EchoAnlegen(Pruefstand s, Func<float, float> pfad)
    {
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        s.Echo = (w, mic) =>
        {
            for (var i = 0; i < mic.Length; i++)
            {
                linie.Enqueue(pfad(0.5f * (w[2 * i] + w[2 * i + 1])));
                mic[i] += linie.Dequeue();
            }
        };
    }

    private static List<(long Beginn, long Ende)> Phasen(List<Gesendet> pakete)
    {
        var phasen = new List<(long, long)>();
        long beginn = -1;
        foreach (var p in pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                var vorlauf = pakete.Count(q => q.Takt == p.Takt && (q.Kennzeichen & Sprachkennzeichen.Preroll) != 0);
                beginn = p.Takt - (2 * vorlauf);
            }
            if ((p.Kennzeichen & Sprachkennzeichen.Ende) != 0 && beginn >= 0)
            {
                phasen.Add((beginn, p.Takt));
                beginn = -1;
            }
        }
        return phasen;
    }

    private sealed class FeldQuelle(float[] x) : IKlangquelle
    {
        private int _pos;

        public QuellenEreignis Lesen(Span<float> ziel)
        {
            for (var i = 0; i < ziel.Length; i++) ziel[i] = _pos + i < x.Length ? x[_pos + i] : 0f;
            _pos += ziel.Length;
            return QuellenEreignis.Aktiv;
        }
    }
}
