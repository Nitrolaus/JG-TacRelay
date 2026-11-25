// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;
using Funk.Tests.Maschine;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Klangfarbe (Q70 bis Q72, 0.9.3) als Stufe für sich: Glocken an ihrer Stelle, lautheitsneutral mit Sprache aus dem Klangkorpus, neutral
/// bitgleich, der Begrenzer dahinter hält die Decke, ein Wechsel der Einstellung springt nicht, ohne Allokation, NaN im Eingang schadet nicht.
/// </summary>
internal static class KlangfarbeTests
{
    private const int R = 480;

    /// <summary>Signal in Rahmen zu 480 durch die Stufe; <paramref name="vorher"/> je Rahmen (Nummer) vor dem Verarbeiten.</summary>
    private static float[] Durch(Klangfarbe k, ReadOnlySpan<float> x, Action<int>? vorher = null)
    {
        var y = new float[x.Length / R * R];
        x[..y.Length].CopyTo(y);
        for (var r = 0; r < y.Length / R; r++)
        {
            vorher?.Invoke(r);
            k.Verarbeiten(y.AsSpan(r * R, R));
        }
        return y;
    }

    private static float[] Sinus(double hz, double amplitude, double sekunden)
    {
        var n = (int)(sekunden * 48000) / R * R;
        var x = new float[n];
        for (var i = 0; i < n; i++) x[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / 48000));
        return x;
    }

    /// <summary>Pegel in dB eines eingeschwungenen Sinus: 0,2 bis 1,0 s, ganze Perioden für 160, 1000, 3000 und 6000 Hz.</summary>
    private static double PegelDb(float[] y) => Pegel.Dbfs(y.AsSpan(9600, 38400));

    private static double SinusPegelDb(KlangfarbeEinstellung e, double hz)
    {
        var k = new Klangfarbe { Einstellung = e };
        return PegelDb(Durch(k, Sinus(hz, 0.1, 1.2)));
    }

    [Test]
    public static void Klangfarbe_Einstellung_AbWerkNeutral_Begrenzt()
    {
        var k = new Klangfarbe();
        Pruefe.Gleich(KlangfarbeEinstellung.Neutral, k.Einstellung, "ab Werk neutral");
        Pruefe.Wahr(k.Einstellung.IstNeutral, "neutral heißt alle drei 0");
        k.Einstellung = new KlangfarbeEinstellung(9, -9, 3);
        Pruefe.Gleich(new KlangfarbeEinstellung(6, -6, 3), k.Einstellung, "auf -6 bis +6 begrenzt");
        k.Einstellung = new KlangfarbeEinstellung(int.MinValue, int.MaxValue, -1);
        Pruefe.Gleich(new KlangfarbeEinstellung(-6, 6, -1), k.Einstellung, "auch ganz außen");
        Pruefe.Gleich(0.0, Klangfarbe.AusgleichDb(KlangfarbeEinstellung.Neutral), "neutral kein Ausgleich");
    }

    [Test]
    public static void Klangfarbe_Neutral_Bitgleich_AuchNachDemZurueckstellen()
    {
        var (sprache, _) = Testszenen.Sprache(4, 71);
        // Laute Stellen dazu, damit auch der Begrenzer etwas zu tun hätte.
        for (var i = 48000; i < 52800; i++) sprache[i] = 0.99f * MathF.Sin(2 * MathF.PI * 160 * i / 48000f);
        var k = new Klangfarbe();
        var y = Durch(k, sprache);
        Pruefe.Wahr(y.AsSpan().SequenceEqual(sprache.AsSpan(0, y.Length)), "neutral: jeder Wert bitgleich");
        Pruefe.Wahr(k.Ueberbrueckt, "neutral überbrückt");

        // Hin und zurück: Nach Blende und offenem Begrenzer ist die Stufe wieder überbrückt und bitgleich.
        k.Einstellung = new KlangfarbeEinstellung(6, 6, 6);
        _ = Durch(k, sprache.AsSpan(0, 48000));
        Pruefe.Falsch(k.Ueberbrueckt, "mit Klangfarbe wird gerechnet");
        k.Einstellung = KlangfarbeEinstellung.Neutral;
        var zurueck = new float[48000];
        sprache.AsSpan(96000, 48000).CopyTo(zurueck);
        var nachher = Durch(k, zurueck);
        Pruefe.Wahr(k.Ueberbrueckt && k.Wirksam.IstNeutral, "zurück auf neutral: wieder überbrückt");
        Pruefe.Wahr(nachher.AsSpan(4800).SequenceEqual(zurueck.AsSpan(4800)), "nach 100 ms wieder bitgleich");
    }

    [Test]
    public static void Klangfarbe_Frequenzgang_GlockenAnIhrerStelle()
    {
        // Sinus bei der Mitte der Glocke gegen 1000 Hz, je Einstellung; der feste Ausgleich gilt für beide gleich und fällt im Abstand weg.
        var bezug = SinusPegelDb(KlangfarbeEinstellung.Neutral, 1000);
        foreach (var (name, hz, mach) in new (string, double, Func<int, KlangfarbeEinstellung>)[]
                 {
                     ("Wärme", Klangfarbe.WaermeHz, db => new(db, 0, 0)),
                     ("Klarheit", Klangfarbe.KlarheitHz, db => new(0, db, 0)),
                     ("Schärfe", Klangfarbe.SchaerfeHz, db => new(0, 0, db)),
                 })
        {
            var neutralAbstand = SinusPegelDb(KlangfarbeEinstellung.Neutral, hz) - bezug;
            PruefeWert.Nahe(0, neutralAbstand, 1e-3, name + ": neutral kein Abstand");
            foreach (var db in new[] { -6, -3, -1, 1, 3, 6 })
            {
                var e = mach(db);
                var mitte = SinusPegelDb(e, hz);
                var tausend = SinusPegelDb(e, 1000);
                var abstand = mitte - tausend;
                // Die Glocke selbst: an ihrer Mitte genau die Einstellung (Pegel gegen neutral, ohne den festen Ausgleich).
                PruefeWert.Nahe(db, mitte - (SinusPegelDb(KlangfarbeEinstellung.Neutral, hz) + Klangfarbe.AusgleichDb(e)), 0.05,
                    $"{name} {db:+0;-0} dB: Anhebung an der Mitte");
                // Gegen 1000 Hz: die Einstellung, abzüglich dessen, was die Flanke der Glocke bei 1000 Hz noch hebt (nach dem Kochbuch gerechnet).
                var flanke = Klangfarbe.FrequenzgangDb(e, 1000) - Klangfarbe.AusgleichDb(e);
                PruefeWert.Nahe(db - flanke, abstand, 0.1, $"{name} {db:+0;-0} dB: Abstand zu 1000 Hz wie gerechnet");
                Console.WriteLine(FormattableString.Invariant($"  {name} {db:+0;-0} dB: Abstand zu 1000 Hz {abstand:0.00} dB (Flanke bei 1000 Hz {flanke:0.00} dB), Ausgleich {Klangfarbe.AusgleichDb(e):0.00} dB"));
                // Klarheit ist absichtlich breit (Q 0,8): Präsenz soll den ganzen Bereich der Mitlaute heben. Ihre Flanke hebt bei 1000 Hz selbst noch
                // gut 1 dB, der Abstand dorthin ist bei +6 also 4,90 dB; geprüft wird sie oben an der Mitte und gegen die gerechnete Flanke
                // (entschieden nach der Prüfung 0.9.3, Modulbericht Klang). Wärme und Schärfe halten zusätzlich +/- 0,5 dB gegen 1000 Hz.
                if (name != "Klarheit") PruefeWert.Nahe(db, abstand, 0.5, $"{name} {db:+0;-0} dB: Abstand zu 1000 Hz = Einstellung +/- 0,5 dB");
            }
        }
    }

    private static readonly KlangfarbeEinstellung[] Pruefstellungen =
    [
        new(6, 0, 0), new(-6, 0, 0), new(0, 6, 0), new(0, -6, 0), new(0, 0, 6), new(0, 0, -6), new(3, 0, 0), new(-3, 0, 0), new(0, 3, 0), new(0, -3, 0),
        new(0, 0, 3), new(0, 0, -3), new(6, 6, 6), new(-6, -6, -6), new(6, -6, 6), new(-6, 6, -6), new(3, 3, 3), new(-3, -3, -3), new(3, 2, -2),
    ];

    [Test]
    public static void Klangfarbe_Ausgleich_NormSprachkurveGenauNeutral()
    {
        // Ein Signal genau nach der Norm-Sprachkurve: je Terzband ein Sinus an der Bandmitte mit dem Bandpegel. Dafür ist der Ausgleich gerechnet,
        // die Leistung muss also bei jeder Einstellung gleich bleiben.
        var n = 48000 / R * R * 2;
        var x = new float[n];
        var zufall = new Random(72);
        var summe = Klangfarbe.BandDb.Sum(db => Math.Pow(10, db / 10));
        for (var b = 0; b < Klangfarbe.BandHz.Length; b++)
        {
            var a = 0.1 * Math.Sqrt(2 * Math.Pow(10, Klangfarbe.BandDb[b] / 10) / summe);
            var phase = zufall.NextDouble() * 2 * Math.PI;
            for (var i = 0; i < n; i++) x[i] += (float)(a * Math.Sin((2 * Math.PI * Klangfarbe.BandHz[b] * i / 48000) + phase));
        }
        var vorher = Pegel.Dbfs(x.AsSpan(48000));
        foreach (var e in Pruefstellungen)
        {
            var y = Durch(new Klangfarbe { Einstellung = e }, x);
            PruefeWert.Nahe(vorher, Pegel.Dbfs(y.AsSpan(48000)), 0.05, $"Norm-Sprachkurve, Wärme {e.WaermeDb}, Klarheit {e.KlarheitDb}, Schärfe {e.SchaerfeDb}");
        }
    }

    [Test]
    public static void Klangfarbe_Lautheitsneutral_MitSpracheAusDemKlangkorpus()
    {
        // Wie die Klangfarbe die Stimme in der Kette bekommt: durch den Hochpass des APM (immer an). Zum Vergleich auch ohne ihn.
        var roh = Klangkorpus.Signal.AsSpan(0, Klangkorpus.Signal.Length / R * R).ToArray();
        var hochpass = MessungKlangB.Hochpass(roh);
        var zeilen = new List<(KlangfarbeEinstellung E, double Kette, double Roh)>();
        foreach (var e in Pruefstellungen)
        {
            double Unterschied(float[] x) => Pegel.Dbfs(Durch(new Klangfarbe { Einstellung = e }, x)) - Pegel.Dbfs(x);
            zeilen.Add((e, Unterschied(hochpass), Unterschied(roh)));
        }
        foreach (var (e, kette, ohneHochpass) in zeilen)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"  {e.WaermeDb:+0;-0;0}/{e.KlarheitDb:+0;-0;0}/{e.SchaerfeDb:+0;-0;0}: Ausgleich {Klangfarbe.AusgleichDb(e):0.00} dB, Korpus {kette:+0.00;-0.00} dB (ohne Hochpass {ohneHochpass:+0.00;-0.00} dB)"));
        }
        // Vorgabe +/- 0,5 dB: Sie hält, solange jeder Regler höchstens 3 dB vom neutralen Stand weg ist. Ganz außen hebt der Korpus mehr als die
        // Norm-Sprachkurve (seine Stimmen haben unten und oben mehr Energie): gemessen bis 0,52 dB mit einem Regler und bis 0,99 dB mit allen drei
        // auf +6 (Korpus vom 19.10.2026; mit dem Korpus davor 0,54 und 0,99 dB, Modulbericht Klang). Dort gilt 1,5 dB: unter der Totzone des Pegelausgleichs beim Empfänger (3 dB), und Gate, Pegelstufe und
        // Mindest-Funklautstärke sehen den Pegel ohnehin vor der Klangfarbe (entschieden nach der Prüfung 0.9.3).
        foreach (var (e, kette, _) in zeilen)
        {
            var aussen = Math.Max(Math.Abs(e.WaermeDb), Math.Max(Math.Abs(e.KlarheitDb), Math.Abs(e.SchaerfeDb))) > 3;
            PruefeWert.Nahe(0, kette, aussen ? 1.5 : 0.5, $"Wärme {e.WaermeDb}, Klarheit {e.KlarheitDb}, Schärfe {e.SchaerfeDb}: Pegel des Klangkorpus wie vorher");
        }
    }

    [Test]
    public static void Klangfarbe_BegrenzerDahinter_HaeltDieDecke()
    {
        // Fast voll ausgesteuert bei 160 Hz und Wärme +6: Die Glocke hebt um 6 dB, der Ausgleich senkt nur um gut 2 dB, Spitzen kämen über 0 dBFS.
        var x = Sinus(160, 0.98, 1.0);
        var e = new KlangfarbeEinstellung(6, 0, 0);
        var ohne = Klangfarbe.FrequenzgangDb(e, 160);
        PruefeWert.Mindestens(3, ohne, "ohne Begrenzer läge die Spitze mehr als 3 dB höher");
        var y = Durch(new Klangfarbe { Einstellung = e }, x);
        PruefeWert.Hoechstens(1.0, Pegel.Spitze(y), "nie über 0 dBFS");
        PruefeWert.Hoechstens(Pegel.DbZuLinear(-0.1f) + 0.01, Pegel.Spitze(y.AsSpan(2 * R)), "nach der Blende an der Decke von -0,1 dBFS");
    }

    /// <summary>Größter Sprung von Wert zu Wert in <paramref name="y"/> zwischen <paramref name="von"/> und <paramref name="bis"/>.</summary>
    private static double Sprung(float[] y, int von, int bis)
    {
        var s = 0.0;
        for (var i = Math.Max(1, von); i < bis; i++) s = Math.Max(s, Math.Abs(y[i] - y[i - 1]));
        return s;
    }

    [Test]
    public static void Klangfarbe_Umstellen_OhneSprung_GegenprobeHart()
    {
        // Ein tiefer Ton mit kleiner Steigung: Jeder Knacks fiele als Sprung von Wert zu Wert auf. Umgestellt wird mitten im Ton.
        var x = Sinus(160, 0.3, 2.0);
        var folge = new KlangfarbeEinstellung[] { new(6, 0, 0), new(-6, 6, -6), new(6, -6, 6), KlangfarbeEinstellung.Neutral, new(0, 0, 6) };
        // Je Wechsel gegen den eigenen ruhigen Ton (Prüfung 0.9.3): Bezug ist der größere Sprung der ruhigen Abschnitte gleich davor (100 ms vor
        // dem Wechsel) und danach (ab 100 ms), geprüft wird nur über die Blende selbst (20 ms ab dem Wechsel, samt beiden Nahtstellen). Bis dahin
        // galt für alle Wechsel der lauteste ruhige Abschnitt; ein Knacks beim leiseren Wechsel neutral auf Schärfe +6 wäre bis gut 70 % über
        // dessen eigener Steigung durchgegangen.
        double[] Lauf(bool ueberblenden)
        {
            var k = new Klangfarbe { Ueberblenden = ueberblenden };
            var y = Durch(k, x, r =>
            {
                if (r % 30 == 10 && r / 30 < folge.Length) k.Einstellung = folge[r / 30];
            });
            var verhaeltnis = new double[folge.Length];
            for (var w = 0; w < folge.Length; w++)
            {
                var um = ((w * 30) + 10) * R;
                var ruhig = Math.Max(Sprung(y, um - (10 * R), um - R), Sprung(y, um + (10 * R), um + (20 * R)));
                verhaeltnis[w] = Sprung(y, um, um + (2 * R) + 1) / ruhig;
            }
            return verhaeltnis;
        }
        var weich = Lauf(true);
        var hart = Lauf(false);
        string Liste(double[] v) => string.Join(", ", v.Select(a => FormattableString.Invariant($"{a:0.00}")));
        Console.WriteLine($"  Sprung beim Umstellen gegen den ruhigen Ton davor und danach, je Wechsel: überblendet {Liste(weich)}; hart {Liste(hart)}");
        for (var w = 0; w < folge.Length; w++)
        {
            PruefeWert.Hoechstens(1.1, weich[w], $"überblendet, Wechsel {w + 1}: kein größerer Sprung als im eigenen ruhigen Ton");
        }
        PruefeWert.Mindestens(2, hart.Max(), "Gegenprobe: hart umgeschaltet springt es");
    }

    [Test]
    public static void Klangfarbe_OhneAllokation_UndNaNSchadetNicht()
    {
        var k = new Klangfarbe();
        var rahmen = new float[R];
        void Rahmen(int n)
        {
            for (var r = 0; r < n; r++)
            {
                for (var i = 0; i < R; i++) rahmen[i] = 0.1f * MathF.Sin(i * 0.07f);
                k.Verarbeiten(rahmen);
            }
        }
        var e = new[] { new KlangfarbeEinstellung(6, 0, -3), new KlangfarbeEinstellung(-2, 4, 1), KlangfarbeEinstellung.Neutral };
        void Folge()
        {
            foreach (var x in e)
            {
                k.Einstellung = x;
                Rahmen(20);
            }
        }
        // Aufwärmen: jeder Weg einmal, damit die Messung nur den Takt sieht.
        Folge();
        var bytes = PruefeWert.Allokiert(Folge);
        Pruefe.Gleich(0L, bytes, "Blenden und Rechnen ohne Allokation");
        k.Einstellung = new KlangfarbeEinstellung(6, 6, 6);
        Rahmen(5);
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        Rahmen(1000);
        Console.WriteLine(FormattableString.Invariant($"  Rechenzeit Klangfarbe mit Begrenzer: {uhr.Elapsed.TotalMicroseconds / 1000:0.0} us je 10 ms (Mittel über 1000 Rahmen)"));

        k.Einstellung = new KlangfarbeEinstellung(6, 6, 6);
        Rahmen(10);
        rahmen.AsSpan().Fill(0.1f);
        rahmen[100] = float.NaN;
        rahmen[200] = float.PositiveInfinity;
        k.Verarbeiten(rahmen);
        Pruefe.Wahr(rahmen.All(float.IsFinite), "nicht endliche Werte kommen nicht durch (der Begrenzer setzt sie auf 0)");
        Rahmen(3);
        Pruefe.Wahr(rahmen.All(float.IsFinite) && Pegel.Dbfs(rahmen) > -40f, "danach färbt sie wieder, der Zustand ist nicht vergiftet");
    }
}
