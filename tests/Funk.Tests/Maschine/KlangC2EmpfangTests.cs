// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangstufe C2a, Punkt 3: die Empfängerseite im Mischer. Derselbe Wächter je ankommendem Funker (die anderen Ströme als Bezug): Ein
/// Funker, der einen anderen echot, und einer, der ohne Sprache dauernd sendet, werden bei mir schrittweise leiser, höchstens bis zur gewählten
/// Stufe; die anderen bleiben unberührt. Zusammenführung im Mischer (stärkste Absenkung zählt, nichts addiert, gemeinsame Untergrenze),
/// Angleichung der Grundlautstärke je Funker (Q29) und die Stufen in Wörtern.
/// </summary>
internal static class KlangC2EmpfangTests
{
    private const int R = 480;

    [Test]
    public static void C2_Stufen_InWoertern_AbWerkDeutlichLeiser()
    {
        Pruefe.Gleich(new DaempfungEinstellung(true, Daempfungsstufe.DeutlichLeiser), DaempfungEinstellung.AbWerk, "ab Werk an, deutlich leiser (Q1, Q26)");
        Pruefe.Gleich(-6f, Daempfungsworte.Db(Daempfungsstufe.EtwasLeiser), "etwas leiser");
        Pruefe.Gleich(-10f, Daempfungsworte.Db(Daempfungsstufe.DeutlichLeiser), "deutlich leiser");
        Pruefe.Gleich(-20f, Daempfungsworte.Db(Daempfungsstufe.FastWeg), "fast weg");
        Pruefe.Gleich("etwas leiser", Daempfungsworte.Wort(Daempfungsstufe.EtwasLeiser), "Wort");
        Pruefe.Gleich("deutlich leiser", Daempfungsworte.Wort(Daempfungsstufe.DeutlichLeiser), "Wort");
        Pruefe.Gleich("fast weg", Daempfungsworte.Wort(Daempfungsstufe.FastWeg), "Wort");
        Pruefe.Gleich("normal", Daempfungsworte.Wort(0f), "keine Dämpfung");
        Pruefe.Gleich("etwas leiser", Daempfungsworte.Wort(-2f), "auf dem Weg");
        Pruefe.Gleich("etwas leiser", Daempfungsworte.Wort(-6f), "-6 dB");
        Pruefe.Gleich("deutlich leiser", Daempfungsworte.Wort(-10f), "-10 dB");
        Pruefe.Gleich("fast weg", Daempfungsworte.Wort(-20f), "-20 dB");
        Pruefe.Gleich("stumm", Daempfungsworte.Wort(float.NegativeInfinity), "stumm");
        foreach (var u in Enum.GetValues<DauersendUrteil>())
        {
            var text = Daempfungsworte.Ursache(u);
            if (u == DauersendUrteil.Keins) Pruefe.Gleich("", text, "kein Urteil, kein Text");
            else Pruefe.Wahr(text.Length > 20 && !text.Contains("dB", StringComparison.Ordinal), $"{u}: Ursache in Wörtern, ohne dB: {text}");
            Pruefe.Falsch(text.Contains('–') || text.Contains('—'), "nur gerade Striche");
        }
    }

    [Test]
    public static void C2_Mischer_Zusammenfuehrung_StaerksteAbsenkungZaehlt_NichtsAddiert_GemeinsameUntergrenze()
    {
        // manuell, Durchsage-Absenkung, Dauersend-Dämpfung, Echo-Dämpfung (eigene Seite), Angleichung
        Pruefe.Gleich(-12f, Mischer.Zusammenfuehren(-10f, -12f, 0f, 0f, 0f), "manuell -10 und Durchsage -12: -12, nicht -22");
        Pruefe.Gleich(-10f, Mischer.Zusammenfuehren(0f, 0f, -10f, -6f, 0f), "Dämpfung -10 und Echo -6: -10");
        Pruefe.Gleich(-10f, Mischer.Zusammenfuehren(-10f, 0f, -10f, 0f, 0f), "manuell -10 und Dämpfung -10: -10");
        Pruefe.Gleich(-10f, Mischer.Zusammenfuehren(0f, 0f, -10f, 0f, -8f), "Angleichung -8 und Dämpfung -10: -10");
        Pruefe.Gleich(-4f, Mischer.Zusammenfuehren(0f, -12f, 0f, 0f, 8f), "leises Mikrofon angehoben (+8), Durchsage senkt von dort ab: -4");
        Pruefe.Gleich(8f, Mischer.Zusammenfuehren(6f, 0f, 0f, 0f, 8f), "Anhebungen auch nicht addiert: +8");
        Pruefe.Gleich(-6f, Mischer.Zusammenfuehren(6f, -12f, 0f, 0f, 0f), "manuell angehoben bleibt wie bisher: +6 - 12");
        Pruefe.Gleich(Mischer.GemeinsameUntergrenzeDb, Mischer.Zusammenfuehren(0f, -30f, -40f, 0f, 0f), "gemeinsame Untergrenze der Automatik");
        Pruefe.Gleich(-40f, Mischer.Zusammenfuehren(-40f, -12f, -10f, 0f, 0f), "was der Funker selbst stellt, bleibt");
        Pruefe.Wahr(float.IsNegativeInfinity(Mischer.Zusammenfuehren(float.NegativeInfinity, 0f, -10f, 0f, 5f)), "stumm bleibt stumm");
        Pruefe.Gleich(0f, Mischer.Zusammenfuehren(0f, 0f, 0f, 0f, 0f), "nichts: 0");
        Pruefe.Gleich(0f, Mischer.Zusammenfuehren(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN), "NaN zählt nicht");
    }

    [Test]
    public static void C2_Empfang_EinFunkerEchotEinenAnderen_WirdSchrittweiseLeiser_BisZurStufe_AndereUnberuehrt()
    {
        foreach (var stufe in new[] { Daempfungsstufe.DeutlichLeiser, Daempfungsstufe.FastWeg })
        {
            var (m, plaetze, verlauf) = EchoSzene(new DaempfungEinstellung(true, stufe), sekunden: 240, out var max);
            var z = m.Zustand(max);
            Console.WriteLine($"  C2 Empfang Echo ({stufe}): Max {z.Urteil}, {z.DaempfungDb:0.0} dB ({z.Wort}), erkannt nach {verlauf.FindIndex(v => v.Urteil == DauersendUrteil.Echo) / 100.0:0.0} s");
            Pruefe.Gleich(DauersendUrteil.Echo, z.Urteil, "Max echot Moritz: Urteil Echo");
            PruefeWert.Nahe(Daempfungsworte.Db(stufe), z.DaempfungDb, 0.05, "höchstens bis zur gewählten Stufe, dort angekommen");
            Pruefe.Gleich(Daempfungsworte.Wort(stufe), z.Wort, "in Wörtern");
            foreach (var p in plaetze.Where(p => p != max))
            {
                Pruefe.Gleich(DauersendUrteil.Keins, m.Zustand(p).Urteil, $"Platz {p}: unberührt");
                Pruefe.Gleich(0f, m.Zustand(p).DaempfungDb, $"Platz {p}: nicht leiser");
            }
            // Schrittweise und ohne Pumpen: je Block höchstens 0,1 dB, und bis zur Stufe nie wieder lauter.
            var d = verlauf.Select(v => v.DaempfungDb).ToList();
            for (var i = 1; i < d.Count; i++) PruefeWert.Hoechstens(0.1 + 1e-4, Math.Abs(d[i] - d[i - 1]), "gerampt");
            var erstes = d.FindIndex(x => x < 0);
            var ganz = d.FindIndex(x => x <= Daempfungsworte.Db(stufe) + 0.05f);
            Pruefe.Wahr(erstes > 0 && ganz > erstes, "erst nach dem Urteil, dann schrittweise");
            for (var i = erstes + 1; i <= ganz; i++) PruefeWert.Hoechstens(d[i - 1] + 1e-4, d[i], "kein Pumpen auf dem Weg nach unten");
            PruefeWert.Mindestens(300, ganz - erstes, "nicht auf einen Schlag (mehrere Schritte)");
        }
        // Schalter aus: Urteil ja (für das gelbe Zeichen), Dämpfung nein.
        var (aus, _, _) = EchoSzene(new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser), sekunden: 180, out var maxAus);
        Pruefe.Gleich(DauersendUrteil.Echo, aus.Zustand(maxAus).Urteil, "aus: Urteil");
        Pruefe.Gleich(0f, aus.Zustand(maxAus).DaempfungDb, "aus: nicht leiser");
    }

    [Test]
    public static void C2_Empfang_DauersendenOhneEcho_WirdLeiser_LangeAnsageNicht()
    {
        // Sprachschätzung der Kandidaten mit RNNoise wie im Programm (KlangMaschine).
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1, Sprachschaetzer = () => new Klang.Kern.RnnoiseSchaetzer() }) { Klick = null };
        var rausch = AnbindungSignale.Rauschen(120 * 48000, -40f, 3);
        var klemmer = m.QuelleHinzufuegen(Funkquelle.Durchgehend(rausch), 0);
        // Eine lange Ansage über die Sprechtaste: eine Phase, Sprache mit kurzen Pausen.
        var ansage = Korpusrede(110, out _, saat: 5, pausenMs: 120);
        var redner = m.QuelleHinzufuegen(new Funkquelle(ansage, [(0, ansage.Length)]), 1);
        var block = new float[R];
        var urteilAb = -1;
        for (var b = 0; b < 110 * 100; b++)
        {
            m.Mischen(block, 1);
            if (urteilAb < 0 && m.Zustand(klemmer).Urteil == DauersendUrteil.Dauersenden) urteilAb = b;
        }
        m.SchaetzerFreigeben();
        Console.WriteLine($"  C2 Dauersenden: erkannt nach {urteilAb / 100.0:0.0} s, {m.Zustand(klemmer).DaempfungDb:0.0} dB; Ansage {m.Zustand(redner).Urteil}");
        Pruefe.Gleich(DauersendUrteil.Dauersenden, m.Zustand(klemmer).Urteil, "durchgehend ohne Sprache: Dauersenden");
        PruefeWert.Mindestens((60 * 100) - 1, urteilAb, "nicht vor einer Minute");
        PruefeWert.Hoechstens(-2, m.Zustand(klemmer).DaempfungDb, "leiser");
        Pruefe.Gleich(DauersendUrteil.Keins, m.Zustand(redner).Urteil, "lange Ansage mit Sprache: kein Dauersenden");
        Pruefe.Gleich(0f, m.Zustand(redner).DaempfungDb, "nicht leiser");
    }

    [Test]
    public static void C2_Angleichung_NurAbwaerts_LangsamNurInPausen_HoechstensZehnDb_EinAusreisserBleibtEinAusreisser()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        // Koordination 04.10.2026 (im Sinn Sarevocs: "sehr laute Sprecher regeln sich zurück"): nur abwärts. Ein Sender mit ausgeschalteter Automatik,
        // deutlich zu laut (Ziel -23 dBFS); dazwischen ein Satz 20 dB leiser (geflüstert).
        var leise = Korpusrede(150, out var phasen, saat: 8, pausenMs: 1500, pegelDb: 16f, schreiNr: 6, schreiDb: -20f);
        var p = m.QuelleHinzufuegen(new Funkquelle(leise, phasen), 0);
        var block = new float[R];
        var verlauf = new List<(float Db, bool Phase)>();
        var q = 0;
        for (var b = 0; b < 150 * 100; b++)
        {
            m.Mischen(block, 1);
            var inPhase = phasen.Any(ph => ph.Von <= b * R && ph.Bis > b * R);
            verlauf.Add((m.Zustand(p).AngleichungDb, inPhase));
            if (b > 0 && verlauf[b].Db != verlauf[b - 1].Db && inPhase && verlauf[b - 1].Phase) q++;
        }
        var ende = verlauf[^1].Db;
        Console.WriteLine($"  C2 Angleichung: am Ende {ende:+0.0;-0.0} dB, Änderungen mitten in einer Phase {q}");
        Pruefe.Gleich(0, q, "nie mitten in einer Phase");
        PruefeWert.Mindestens(-10f - 1e-4, ende, "höchstens 10 dB leiser");
        PruefeWert.Hoechstens(-8f, ende, "der laute Sender wird fast ans Ziel zurückgeregelt (begrenzt auf 10 dB)");
        for (var i = 1; i < verlauf.Count; i++) PruefeWert.Hoechstens(1f + 1e-4, Math.Abs(verlauf[i].Db - verlauf[i - 1].Db), "je Pause höchstens 1 dB");
        var schrei = SchreiPhase(phasen, 6);
        var vorSchrei = verlauf[schrei.Von / R - 1].Db;
        var nachSchrei = verlauf[Math.Min(verlauf.Count - 1, schrei.Bis / R + 150)].Db;
        PruefeWert.Hoechstens(vorSchrei + 1e-4, nachSchrei, "ein geflüsterter Satz zieht die Angleichung nicht hoch");

        // Ein leiser Sender wird nicht angehoben: Über Lautsprecher machte das mehr Echo und kostete eigene Anlaute (Prüfstand, Gefecht 25).
        var m2 = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var zuLeise = Korpusrede(60, out var lp, saat: 9, pausenMs: 1500, pegelDb: -16f);
        var pl = m2.QuelleHinzufuegen(new Funkquelle(zuLeise, lp), 0);
        var hoechste = float.NegativeInfinity;
        for (var b = 0; b < 60 * 100; b++)
        {
            m2.Mischen(block, 1);
            hoechste = MathF.Max(hoechste, m2.Zustand(pl).AngleichungDb);
        }
        Console.WriteLine($"  C2 Angleichung leiser Sender: höchstens {hoechste:+0.0;-0.0} dB");
        Pruefe.Gleich(0f, hoechste, "leiser Sender: keine Anhebung");

        // Aus: keine Angleichung. Seit 0.9.0 (Q48) ist das der eigene Schalter Pegelausgleich, bis 0.8.x war es der Schalter der Dämpfung.
        var aus = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        aus.Waechter.AngleichungAn = false;
        var pa = aus.QuelleHinzufuegen(new Funkquelle(leise, phasen), 0);
        for (var b = 0; b < 60 * 100; b++) aus.Mischen(block, 1);
        Pruefe.Gleich(0f, aus.Zustand(pa).AngleichungDb, "Schalter aus: keine Angleichung");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Hilfen
    // ------------------------------------------------------------------------------------------------------------------------------

    private static (int Von, int Bis) SchreiPhase(List<(int Von, int Bis)> phasen, int nr) => phasen[nr];

    /// <summary>
    /// Rede aus dem Klangkorpus: Äußerungen hintereinander (zufällig gewählt), je <paramref name="pausenMs"/> Pause, auf <paramref name="pegelDb"/>
    /// gegenüber dem Korpus; die Äußerung Nummer <paramref name="schreiNr"/> um <paramref name="schreiDb"/> lauter. Phasen in Samples.
    /// </summary>
    internal static float[] Korpusrede(int sekunden, out List<(int Von, int Bis)> phasen, int saat, int pausenMs, float pegelDb = 0f, int schreiNr = -1, float schreiDb = 0f,
        bool nurFunklaut = false, bool nurGleichmaessig = false)
    {
        var r = new Random(saat);
        var n = sekunden * 48000;
        var x = new float[n];
        phasen = [];
        var pos = 4800;
        var korpus = Klangkorpus.Signal;
        var nr = 0;
        // Nur Sprüche in Funklautstärke: ohne die leisen (zur Familie, -37 bis -42 dBFS) und die lauten (gerufen) des Korpus.
        // Gleichmäßig: nur Sätze mit Hülle normal oder abfallend (wie die leisen Szenen des Prüfstands); anschwellende enden um Funkniveau, auch 12 dB leiser.
        var auswahl = nurGleichmaessig ? [.. Klangkorpus.Aeusserungen.Where(a => a.Huelle is "normal" or "abfallend")]
            : nurFunklaut ? [.. Klangkorpus.Aeusserungen.Where(a => a.Huelle is not ("leise" or "laut"))] : Klangkorpus.Aeusserungen;
        while (pos < n)
        {
            var a = auswahl[r.Next(auswahl.Count)];
            var laenge = Math.Min(a.Ende - a.Start, n - pos);
            var g = Pegel.DbZuLinear(pegelDb + (nr == schreiNr ? schreiDb : 0f));
            for (var i = 0; i < laenge; i++) x[pos + i] = korpus[a.Start + i] * g;
            phasen.Add((pos, pos + laenge));
            pos += laenge + pausenMs * 48;
            nr++;
        }
        return x;
    }

    /// <summary>
    /// Vier Funker im Mischer: Moritz redet, Max echot ihn (Moritz' Stimme 200 ms später, 6 dB leiser, Phase öffnet 150 ms nach Moritz' Anfang
    /// und schließt 300 ms nach seinem Ende), zwei andere reden unabhängig, auch über die anderen. Rückgabe: Mischer, Plätze, Verlauf von Max.
    /// </summary>
    private static (Mischer M, int[] Plaetze, List<FunkerZustand> Verlauf) EchoSzene(DaempfungEinstellung e, int sekunden, out int max)
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null, Daempfung = e };
        var moritz = Korpusrede(sekunden, out var mp, saat: 31, pausenMs: 2500);
        var echo = new float[moritz.Length];
        var ep = new List<(int, int)>();
        foreach (var (von, bis) in mp)
        {
            var a = von + 150 * 48;
            var b = Math.Min(echo.Length, bis + 200 * 48 + 300 * 48);
            for (var i = a; i < b; i++) echo[i] = (i - 200 * 48 >= 0 ? moritz[i - 200 * 48] * 0.5f : 0f) + 0.0005f * MathF.Sin(i * 0.3f);
            ep.Add((a, b));
        }
        var anna = Korpusrede(sekunden, out var ap, saat: 32, pausenMs: 4000, pegelDb: -2f);
        var olaf = Korpusrede(sekunden, out var op, saat: 33, pausenMs: 6000, pegelDb: 2f);
        int[] plaetze = [m.QuelleHinzufuegen(new Funkquelle(moritz, mp), 0), m.QuelleHinzufuegen(new Funkquelle(echo, ep), 0),
            m.QuelleHinzufuegen(new Funkquelle(anna, ap), 0), m.QuelleHinzufuegen(new Funkquelle(olaf, op), 0)];
        max = plaetze[1];
        var block = new float[R];
        var verlauf = new List<FunkerZustand>();
        for (var b = 0; b < sekunden * 100; b++)
        {
            m.Mischen(block, 1);
            verlauf.Add(m.Zustand(max));
        }
        return (m, plaetze, verlauf);
    }
}
