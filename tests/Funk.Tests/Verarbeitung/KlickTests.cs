// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Der Funk-Klick (A11, seit 0.5.0 einstellbar nach A63, Klang seit 0.6.0 Stufe 2b): ein kurzes rosa Rauschen statt der Klänge
/// Klick, Doppelklick und Piep, nach Sarevocs Wahl Variante A (30 ms beim Drücken, 90 ms beim Loslassen, Band 900 bis 3500 Hz,
/// Anstieg 3 ms, dann exponentiell abfallend). Jeder Abspielvorgang bekommt ein neues Rauschen, sonst klingt es mechanisch; mit
/// Startwert ist die Folge für Tests fest. Eigene Taste und andere Sprecher getrennt an oder aus und je mit Lautstärke, bei
/// Sprachaktivierung dezent oder aus, "Anhören". Die Lautstärke ist der Pegel über 200 ms (<see cref="FunkKlick.PegelUeber200Ms"/>)
/// bezogen auf die Sprache hinter der Kette, wie seit 0.5.0.
/// </summary>
internal static class KlickTests
{
    private static readonly KlickArt[] Arten = [KlickArt.Druck, KlickArt.Loslassen];

    private static float Pegel200(ReadOnlySpan<float> x) => FunkKlick.PegelUeber200Ms(x);

    /// <summary>Was ein Klick allein ergibt: einmal 200 ms mono gemischt (mono heißt: mittig in voller Höhe).</summary>
    private static float[] Gemischt(FunkKlick k)
    {
        var l = new float[9600];
        k.Mischen(l, Span<float>.Empty);
        return l;
    }

    /// <summary>Ein Rauschen, wie der Klick es spielt, bei 0 dB (über 200 ms so laut wie die Sprache).</summary>
    internal static float[] Rauschen(KlickArt art, Random zufall)
    {
        var x = new float[FunkKlick.Laenge(art)];
        var n = FunkKlick.Erzeugen(art, zufall, x);
        return x[..n];
    }

    /// <summary>Ähnlichkeit zweier Signale gleicher Länge, -1 bis 1 (Korrelationskoeffizient ohne Mittelwertabzug).</summary>
    private static double Aehnlichkeit(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            ab += (double)a[i] * b[i];
            aa += (double)a[i] * a[i];
            bb += (double)b[i] * b[i];
        }
        return aa <= 0 || bb <= 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }

    /// <summary>
    /// Nachbau der Vorlage, nach der Sarevoc gewählt hat (tmp\funkklick\varianten.ps1, Funktion Rauschen, Variante A_kurz_hell), ohne
    /// deren Pegelung: rosa Rauschen nach Paul Kellet aus <c>Random(saat).NextDouble()</c>, je zwei Biquads Hochpass 900 Hz und
    /// Tiefpass 3500 Hz mit Q 0,707 (Direktform I, Anfangszustand null), Sinusflanke über 3 ms, danach exp(-4 t / T) bis zum Ende.
    /// </summary>
    internal static double[] Vorlage(double ms, int saat)
    {
        const double rate = 48000;
        var n = (int)Math.Round(rate * ms / 1000);
        var r = new Random(saat);
        var p = new double[n];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (var i = 0; i < n; i++)
        {
            var w = (r.NextDouble() * 2) - 1;
            b0 = (0.99886 * b0) + (w * 0.0555179);
            b1 = (0.99332 * b1) + (w * 0.0750759);
            b2 = (0.96900 * b2) + (w * 0.1538520);
            b3 = (0.86650 * b3) + (w * 0.3104856);
            b4 = (0.55000 * b4) + (w * 0.5329522);
            b5 = (-0.7616 * b5) - (w * 0.0168980);
            p[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + (w * 0.5362);
            b6 = w * 0.115926;
        }
        p = Biquad(Biquad(Biquad(Biquad(p, hoch: true, 900), hoch: true, 900), hoch: false, 3500), hoch: false, 3500);
        var an = (int)Math.Round(rate * 3 / 1000);
        for (var i = 0; i < n; i++)
            p[i] *= i < an ? Math.Sin(Math.PI / 2 * i / an) : Math.Exp(-4.0 * (i - an) / Math.Max(1, n - an));
        return p;

        static double[] Biquad(double[] x, bool hoch, double f)
        {
            var w = 2 * Math.PI * f / rate;
            var c = Math.Cos(w);
            var a = Math.Sin(w) / (2 * 0.707);
            double k0, k1, k2;
            if (hoch) (k0, k1, k2) = ((1 + c) / 2, -(1 + c), (1 + c) / 2);
            else (k0, k1, k2) = ((1 - c) / 2, 1 - c, (1 - c) / 2);
            double a0 = 1 + a, a1 = -2 * c, a2 = 1 - a;
            var y = new double[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < x.Length; i++)
            {
                var v = ((k0 * x[i]) + (k1 * x1) + (k2 * x2) - (a1 * y1) - (a2 * y2)) / a0;
                (x2, x1, y2, y1) = (x1, x[i], y1, v);
                y[i] = v;
            }
            return y;
        }
    }

    /// <summary>Leistung bei <paramref name="hz"/> (Goertzel), für den Blick ins Spektrum.</summary>
    private static double Leistung(ReadOnlySpan<float> x, double hz)
    {
        var k = 2 * Math.Cos(2 * Math.PI * hz / 48000);
        double s1 = 0, s2 = 0;
        foreach (var v in x)
        {
            var s = v + (k * s1) - s2;
            (s2, s1) = (s1, s);
        }
        return (s1 * s1) + (s2 * s2) - (k * s1 * s2);
    }

    /// <summary>
    /// Sarevoc wählte am 02.10.2026 nach Hörproben Variante A. Mit demselben Startwert ist der Klick Sample für Sample das Rauschen der
    /// Vorlage, nur anders gepegelt (Startwerte 11, 31, 12 und 33 wie in der Hörprobe funkklick-A_kurz_hell.wav).
    /// </summary>
    [Test]
    public static void Vorlage_VarianteA_GenauNachgebaut()
    {
        foreach (var (art, ms, saat) in new[] { (KlickArt.Druck, 30.0, 11), (KlickArt.Loslassen, 90.0, 31), (KlickArt.Druck, 30.0, 12), (KlickArt.Loslassen, 90.0, 33) })
        {
            var x = Rauschen(art, new Random(saat));
            var v = Vorlage(ms, saat);
            var was = $"{art}, Startwert {saat}";
            Pruefe.Gleich(v.Length, x.Length, was + ": Länge wie die Vorlage");
            double xv = 0, vv = 0;
            for (var i = 0; i < v.Length; i++)
            {
                xv += x[i] * v[i];
                vv += v[i] * v[i];
            }
            var faktor = xv / vv;
            PruefeWert.Mindestens(1e-6, faktor, was + ": gleiche Richtung");
            var abweichung = 0.0;
            for (var i = 0; i < v.Length; i++) abweichung = Math.Max(abweichung, Math.Abs(x[i] - (faktor * v[i])));
            PruefeWert.Hoechstens(1e-5 * Pegel.Spitze(x), abweichung, was + ": Sample für Sample die Vorlage");
        }
    }

    [Test]
    public static void Rauschen_DauerHuellkurveBandUndPegel()
    {
        Pruefe.Gleich(30 * 48, FunkKlick.Laenge(KlickArt.Druck), "Drücken 30 ms");
        Pruefe.Gleich(90 * 48, FunkKlick.Laenge(KlickArt.Loslassen), "Loslassen 90 ms");
        var zufall = new Random(2026);
        foreach (var art in Arten)
        {
            var n = FunkKlick.Laenge(art);
            double spitzeFenster = 0, endeFenster = 0;
            for (var r = 0; r < 50; r++)
            {
                var x = Rauschen(art, zufall);
                Pruefe.Gleich(n, x.Length, art + ": volle Länge");
                PruefeWert.Nahe(FunkKlickEinstellungen.SprachpegelDbfs, Pegel200(x), 0.01, art + ": bei 0 dB über 200 ms so laut wie Sprache");
                Pruefe.Gleich(0f, x[0], art + ": beginnt bei null (Sinusflanke)");
                spitzeFenster += Pegel.Leistung(x.AsSpan(3 * 48, 3 * 48));
                endeFenster += Pegel.Leistung(x.AsSpan(n - (n / 10)));
            }
            // Anstieg 3 ms, danach exp(-4 t / T): Im letzten Zehntel ist die Hülle auf exp(-3,6) bis exp(-4) gefallen, gut 31 dB.
            var abfall = 10 * Math.Log10(spitzeFenster / endeFenster);
            Console.WriteLine($"  {art}: letztes Zehntel {abfall:0.0} dB unter 3 bis 6 ms");
            Pruefe.Wahr(abfall is > 25 and < 40, $"{art}: fällt exponentiell ab ({abfall:0.0} dB)");
        }

        // Band 900 bis 3500 Hz: über 100 Klicks gemittelt liegt die Mitte weit über den Rändern.
        var hz = new[] { 150.0, 400.0, 1800.0, 2600.0, 8000.0, 16000.0 };
        var summe = new double[hz.Length];
        for (var r = 0; r < 100; r++)
        {
            var x = Rauschen(KlickArt.Loslassen, zufall);
            for (var f = 0; f < hz.Length; f++) summe[f] += Leistung(x, hz[f]);
        }
        var db = summe.Select(s => 10 * Math.Log10(s / summe[2])).ToArray();
        Console.WriteLine("  Spektrum gegen 1800 Hz: " + string.Join(", ", hz.Select((f, i) => $"{f:0} Hz {db[i]:0.0} dB")));
        PruefeWert.Mindestens(-6, db[3], "2600 Hz im Band");
        PruefeWert.Hoechstens(-15, db[1], "400 Hz unter dem Band");
        PruefeWert.Hoechstens(-15, db[4], "8 kHz über dem Band");
        PruefeWert.Hoechstens(-30, db[0], "150 Hz weit unter dem Band");
        PruefeWert.Hoechstens(-30, db[5], "16 kHz weit über dem Band");
    }

    [Test]
    public static void Rauschen_JedesMalNeu_MitStartwertWiederholbar()
    {
        static (float[] Erster, float[] Zweiter) ZweiMal(FunkKlick k)
        {
            k.Anfordern(KlickArt.Druck);
            var erster = Gemischt(k);
            k.Anfordern(KlickArt.Druck);
            return (erster, Gemischt(k));
        }

        var (a1, a2) = ZweiMal(new FunkKlick(saat: 5));
        var aehnlich = Aehnlichkeit(a1, a2);
        Console.WriteLine($"  zwei Klicks nacheinander: Ähnlichkeit {aehnlich:0.00}");
        PruefeWert.Hoechstens(0.5, Math.Abs(aehnlich), "zweimal gedrückt: ein anderes Rauschen");
        PruefeWert.Nahe(Pegel200(a1), Pegel200(a2), 0.01, "und gleich laut");
        var (b1, b2) = ZweiMal(new FunkKlick(saat: 5));
        Pruefe.Wahr(a1.SequenceEqual(b1) && a2.SequenceEqual(b2), "derselbe Startwert: dieselbe Folge");
        var (c1, _) = ZweiMal(new FunkKlick(saat: 6));
        PruefeWert.Hoechstens(0.5, Math.Abs(Aehnlichkeit(a1, c1)), "anderer Startwert: anderes Rauschen");
        var (d1, _) = ZweiMal(new FunkKlick());
        var (e1, _) = ZweiMal(new FunkKlick());
        PruefeWert.Hoechstens(0.5, Math.Abs(Aehnlichkeit(d1, e1)), "ohne Startwert: jede Instanz ihr eigenes Rauschen");
    }

    /// <summary>
    /// Die Voreinstellung (A63: ab Werk hörbar, aber dezent) bleibt: eigene Taste 12 dB, andere Sprecher 15 dB unter der Sprache
    /// hinter der Kette (-25 dBFS über 200 ms, Mitte von -35 bis -15 dBFS, GateVorgaben), bei Sprachaktivierung weitere 18 dB
    /// leiser (A11: "sehr dezent"). Das Rauschen ist auf denselben Bezug geeicht wie der Klick bis 0.5.x.
    /// </summary>
    [Test]
    public static void Voreinstellung_HoerbarAberDezent()
    {
        var k = new FunkKlick(saat: 1);
        var e = k.Einstellungen;
        Pruefe.Wahr(e is { EigeneAn: true, AndereAn: true, SprachaktivierungAus: false }, "an, bei Sprachaktivierung dezent");
        Pruefe.Gleich(-12f, e.EigeneDb, "eigene Taste 12 dB unter der Sprache");
        Pruefe.Gleich(-15f, e.AndereDb, "andere Sprecher 15 dB unter der Sprache");
        Pruefe.Gleich(-18f, e.SprachaktivierungDb, "Sprachaktivierung 18 dB leiser");

        k.Anfordern(KlickArt.Druck);
        var eigen = Gemischt(k);
        k.Ausloesen(KlickArt.Druck, sprachaktivierung: false);
        var andere = Pegel200(Gemischt(k));
        k.Ausloesen(KlickArt.Druck, sprachaktivierung: true);
        var va = Pegel200(Gemischt(k));
        Console.WriteLine($"  Klick über 200 ms: eigene Taste {Pegel200(eigen):0.0} dBFS, andere {andere:0.0}, andere bei Sprachaktivierung {va:0.0} dBFS (Sprache -25 dBFS)");
        PruefeWert.Nahe(-37, Pegel200(eigen), 0.05, "eigene Taste: 12 dB unter der Sprache");
        PruefeWert.Nahe(-40, andere, 0.05, "andere Sprecher");
        PruefeWert.Nahe(-58, va, 0.05, "andere bei Sprachaktivierung");
    }

    /// <summary>
    /// Ein Rauschen hat keine feste Spitze: Mal liegt sie höher, mal tiefer. Der Begrenzer hinter dem Mischer setzt bei -2 dBFS
    /// Spitze ein (Mitte des Knies -1, Breite 2). Auch ganz laut (-6 dB) und mittig in voller Höhe (mono) darf kein Klick allein ihn
    /// berühren, über je 2000 Rauschen gemessen; die Werte stehen in der Ausgabe.
    /// </summary>
    [Test]
    public static void Spitze_LoestDenBegrenzerNieAus()
    {
        var b = new BegrenzerEinstellungen();
        var knie = b.SchwelleDbfs - (b.KniebreiteDb / 2);
        var zufall = new Random(77);
        foreach (var art in Arten)
        {
            var spitzen = new List<float>();
            var rms = new List<float>();
            for (var r = 0; r < 2000; r++)
            {
                var x = Rauschen(art, zufall);
                spitzen.Add(Pegel.LinearZuDb(Pegel.Spitze(x)));
                rms.Add(Pegel.Dbfs(x));
            }
            spitzen.Sort();
            var hoechste = spitzen[^1];
            var mitte = spitzen[spitzen.Count / 2];
            Console.WriteLine($"  {art} bei 0 dB: Spitze Mitte {mitte:0.0}, höchste {hoechste:0.0} dBFS; RMS über die Dauer {rms.Average():0.0} dBFS, über 200 ms {FunkKlickEinstellungen.SprachpegelDbfs:0.0}; "
                + $"ab Werk eigene Taste höchste Spitze {hoechste + FunkKlickEinstellungen.EigeneVoreinstellungDb:0.0}, ganz laut {hoechste + FunkKlickEinstellungen.MaxDb:0.0} dBFS (Knie ab {knie:0.0})");
            PruefeWert.Hoechstens(knie - 0.5, hoechste + FunkKlickEinstellungen.MaxDb, art + ": ganz laut unter dem Knie des Begrenzers");
        }
    }

    [Test]
    public static void EigeneUndAndere_GetrenntSchaltbarUndEinstellbar()
    {
        static float Eigener(FunkKlickEinstellungen e)
        {
            var k = new FunkKlick(e, saat: 3);
            k.Anfordern(KlickArt.Druck);
            return Pegel200(Gemischt(k));
        }
        static float Anderer(FunkKlickEinstellungen e, bool va)
        {
            var k = new FunkKlick(e, saat: 4);
            k.Ausloesen(KlickArt.Druck, va);
            return Pegel200(Gemischt(k));
        }
        const float Sprache = FunkKlickEinstellungen.SprachpegelDbfs;
        PruefeWert.Nahe(Sprache - 20, Eigener(new FunkKlickEinstellungen { EigeneDb = -20f }), 0.05, "eigene Lautstärke");
        PruefeWert.Nahe(Sprache - 6, Eigener(new FunkKlickEinstellungen { EigeneDb = -6f, AndereAn = false }), 0.05, "eigene laut, andere aus");
        Pruefe.Gleich(Pegel.MinDbfs, Eigener(new FunkKlickEinstellungen { EigeneAn = false }), "eigener Klick aus");
        PruefeWert.Nahe(Sprache - 8, Anderer(new FunkKlickEinstellungen { AndereDb = -8f, EigeneAn = false }, false), 0.05, "andere Lautstärke, eigene aus");
        PruefeWert.Nahe(Sprache - 8 - 18, Anderer(new FunkKlickEinstellungen { AndereDb = -8f }, true), 0.05, "andere bei Sprachaktivierung dezent");
        PruefeWert.Nahe(Sprache - 15 - 10, Anderer(new FunkKlickEinstellungen { SprachaktivierungDb = -10f }, true), 0.05, "dezent einstellbar");
        Pruefe.Gleich(Pegel.MinDbfs, Anderer(new FunkKlickEinstellungen { SprachaktivierungAus = true }, true), "bei Sprachaktivierung aus");
        PruefeWert.Nahe(Sprache - 15, Anderer(new FunkKlickEinstellungen { SprachaktivierungAus = true }, false), 0.05, "mit Taste trotzdem");
        Pruefe.Gleich(Pegel.MinDbfs, Anderer(new FunkKlickEinstellungen { AndereAn = false }, false), "andere aus");
        Pruefe.Gleich(Pegel.MinDbfs, Anderer(new FunkKlickEinstellungen { AndereAn = false }, true), "andere aus, auch bei Sprachaktivierung");
    }

    [Test]
    public static void Sprachaktivierung_LeiseOderAus()
    {
        var k = new FunkKlick(saat: 9);
        var l = new float[4800];
        var r = new float[4800];
        k.Ausloesen(KlickArt.Loslassen, sprachaktivierung: true);
        k.Mischen(l, r);
        var erwartet = Pegel.Spitze(Rauschen(KlickArt.Loslassen, new Random(9))) * Pegel.DbZuLinear(k.Einstellungen.AndereDb + k.Einstellungen.SprachaktivierungDb) * 0.70710678;
        PruefeWert.Nahe(erwartet, Pegel.Spitze(l), 1e-6, "18 dB leiser als andere mit Taste, mittig");

        var aus = new FunkKlick(new FunkKlickEinstellungen { SprachaktivierungAus = true });
        Array.Clear(l);
        aus.Ausloesen(KlickArt.Druck, sprachaktivierung: true);
        aus.Mischen(l, r);
        Pruefe.Gleich(0f, Pegel.Spitze(l), "bei Sprachaktivierung aus");
    }

    /// <summary>Einsätze im Mischen: Beginn nach mindestens 50 ms Ruhe (in ms) und Pegel über 200 ms ab dort.</summary>
    private static List<(int Ms, float Pegel)> Einsaetze(FunkKlick k, int ms = 2000)
    {
        var alles = new float[ms * 48];
        var block = new float[480];
        for (var b = 0; b < ms / 10; b++)
        {
            Array.Clear(block);
            k.Mischen(block, Span<float>.Empty);
            block.CopyTo(alles, b * 480);
        }
        var einsaetze = new List<(int, float)>();
        var ruhe = int.MaxValue;
        for (var i = 0; i < alles.Length; i++)
        {
            if (alles[i] == 0f)
            {
                ruhe = ruhe == int.MaxValue ? ruhe : ruhe + 1;
                continue;
            }
            if (ruhe >= 50 * 48) einsaetze.Add((i / 48, Pegel200(alles.AsSpan(i, Math.Min(9600, alles.Length - i)))));
            ruhe = 0;
        }
        return einsaetze;
    }

    [Test]
    public static void Anhoeren_EigeneDannAndere_InIhrerLautstaerke()
    {
        var k = new FunkKlick(new FunkKlickEinstellungen { EigeneDb = -10f, AndereDb = -20f });
        Pruefe.Wahr(k.AnhoerenAnfordern(), "es spielt etwas");
        var e = Einsaetze(k);
        Pruefe.Gleich(4, e.Count, "eigene drücken und loslassen, dann andere: " + string.Join(", ", e));
        foreach (var (i, ms, pegel) in new[] { (0, 0, -35f), (1, 300, -35f), (2, 800, -45f), (3, 1100, -45f) })
        {
            PruefeWert.Hoechstens(10, Math.Abs(e[i].Ms - ms), $"Einsatz {i} bei {ms} ms: {e[i].Ms}");
            PruefeWert.Nahe(pegel, e[i].Pegel, 0.05, $"Einsatz {i} in seiner Lautstärke");
        }

        var nurAndere = new FunkKlick(new FunkKlickEinstellungen { EigeneAn = false });
        Pruefe.Wahr(nurAndere.AnhoerenAnfordern(), "andere an");
        var n = Einsaetze(nurAndere);
        Pruefe.Wahr(n.Count == 2 && n[0].Ms == 0 && Math.Abs(n[1].Ms - 300) <= 10 && Math.Abs(n[0].Pegel + 40f) < 0.05, "nur andere, gleich am Anfang: " + string.Join(", ", n));

        var keiner = new FunkKlick(new FunkKlickEinstellungen { EigeneAn = false, AndereAn = false });
        Pruefe.Falsch(keiner.AnhoerenAnfordern(), "beide aus: nichts zu hören");
        Pruefe.Gleich(0, Einsaetze(keiner).Count, "still");
    }

    /// <summary>Das neue Rauschen entsteht im Audio-Thread beim Start eines Klicks, in Puffern aus dem Konstruktor.</summary>
    [Test]
    public static void Mischen_MitAnhoerenUndKlicks_AllokiertNicht()
    {
        var k = new FunkKlick();
        var l = new float[480];
        var r = new float[480];
        k.AnhoerenAnfordern();
        k.Mischen(l, r);
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                if (i % 40 == 0)
                {
                    k.Anfordern(KlickArt.Druck);
                    k.Ausloesen(KlickArt.Loslassen, sprachaktivierung: i % 80 == 0);
                }
                if (i == 150) k.AnhoerenAnfordern();
                k.Mischen(l, r);
            }
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes beim Mischen");
    }

    [Test]
    public static void Einstellungen_UngueltigeWerte_Abgelehnt()
    {
        foreach (var (was, e) in new (string, FunkKlickEinstellungen)[]
        {
            ("eigene NaN", new FunkKlickEinstellungen { EigeneDb = float.NaN }),
            ("eigene zu laut", new FunkKlickEinstellungen { EigeneDb = FunkKlickEinstellungen.MaxDb + 0.5f }),
            ("andere zu leise", new FunkKlickEinstellungen { AndereDb = FunkKlickEinstellungen.MinDb - 0.5f }),
            ("andere unendlich", new FunkKlickEinstellungen { AndereDb = float.NegativeInfinity }),
            ("Sprachaktivierung lauter", new FunkKlickEinstellungen { SprachaktivierungDb = 3f }),
            ("Sprachaktivierung NaN", new FunkKlickEinstellungen { SprachaktivierungDb = float.NaN }),
        })
        {
            Pruefe.Wirft<ArgumentException>(() => _ = new FunkKlick(e), was);
        }
    }

    [Test]
    public static void UeberBlockgrenzen_VollstaendigUndEinmalig()
    {
        var k = new FunkKlick(saat: 42);
        var gesamt = new List<float>();
        var l = new float[100];
        k.Anfordern(KlickArt.Loslassen); // aus fremdem Thread erlaubt
        for (var b = 0; b < 50; b++)
        {
            Array.Clear(l);
            k.Mischen(l, Span<float>.Empty);
            gesamt.AddRange(l);
        }
        var faktor = Pegel.DbZuLinear(k.Einstellungen.EigeneDb);
        var klang = Rauschen(KlickArt.Loslassen, new Random(42));
        for (var i = 0; i < klang.Length; i++) PruefeWert.Nahe(klang[i] * faktor, gesamt[i], 1e-7, $"Sample {i}");
        Pruefe.Wahr(gesamt.Skip(klang.Length).All(v => v == 0f), "danach still");
    }
}
