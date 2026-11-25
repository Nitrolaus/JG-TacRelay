// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Nacharbeit aus der Gegenprüfung der Klangstufe C1 (output\test\20261004_pruefung-c1.md), im Kern: H1 (Anker der Mindest-Funklautstärke aus der
/// gemessenen Stimme, nie aus einer begrenzten Verstärkung), M1 (der Korridor hält seine Mindestbreite, auch wenn die Stimme in der Sitzung
/// sinkt) und M4 (fest eingemessen gilt nur für das Mikrofon, auf dem eingemessen wurde). Vorlage sind die Proben A, B und C der Prüfung
/// (tmp\tr-pruef-c1\PruefC1Proben.cs); der Client-Teil steht in <c>OberflaecheKlangC1Tests</c>.
/// </summary>
internal static class KlangC1NacharbeitTests
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private const int R = 480;

    private sealed record Zahl(int Woerter, int Cut30, int Cut80, int Gesendet, int Rahmen, int Luecken = 0, int EndeFehlt = 0);

    private static async Task<(List<TelemetrieEintrag> T, KorridorStand K, string? Eingang)> Lauf(LautstaerkeEinstellung l, float abstand, float mikrofonDb, ulong saat,
        int wiederholungen = 1, bool waechter = true)
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Lautstaerke = l;
        s.Maschine.Daempfung = new DaempfungEinstellung(waechter, Daempfungsstufe.DeutlichLeiser);
        s.Maschine.Gate = GateVorgaben.Standard with { MindestAbstandDb = abstand };
        await s.StartenAsync();
        var e = Pumpmessung.Eingang(mikrofonDb, saat, Pumpmessung.GrundrauschenDbfs + mikrofonDb);
        for (var i = 0; i < wiederholungen; i++)
        {
            if (i == wiederholungen - 1) s.Telemetrie.Clear();
            s.Abspielen(e);
        }
        return ([.. s.Telemetrie], s.Maschine.Korridor, s.Maschine.Telemetrie.Eingang);
    }

    /// <summary>Normale Äußerungen des Korpus: Wortanfänge mit fehlenden ersten 30 ms, mehr als 80 ms fehlend, Rahmen gesendet.</summary>
    private static Zahl Zaehlen(List<TelemetrieEintrag> t)
    {
        var n = t.Count;
        var g = Pumpmessung.GesendeteRahmen(t);
        var v = Pumpmessung.VerzugGate;
        int w = 0, c30 = 0, c80 = 0, rahmen = 0, ges = 0, luecken = 0, ende = 0;
        foreach (var a in Klangkorpus.Aeusserungen)
        {
            if (a.Huelle == "leise") continue;
            // Lücke: ein Wort nach dem ersten gesendeten Rahmen der Äußerung angeschnitten; Ende: die letzten 150 ms nicht ganz gesendet.
            var von = (a.Start + v) / R;
            var erster = Enumerable.Range(von, Math.Max(0, Math.Min(n, ((a.Ende + v) / R) + 1) - von)).Where(f => g[f]).DefaultIfEmpty(int.MaxValue).First();
            if (Enumerable.Range((a.Ende + v - 7200) / R, 15).Any(f => f >= n || !g[f])) ende++;
            foreach (var wort in a.Woerter)
            {
                w++;
                var f0 = (wort + v) / R;
                var f1 = (wort + v + 1440 - 1) / R;
                var cut = Enumerable.Range(f0, f1 - f0 + 1).Any(f => f >= n || !g[f]);
                if (cut) c30++;
                if (cut && f0 > erster) luecken++;
                var f80 = (wort + v + 3840) / R;
                if (f80 >= n || !g[f80]) c80++;
            }
            for (var f = (a.Start + v) / R; f < Math.Min(n, ((a.Ende + v) / R) + 1); f++)
            {
                rahmen++;
                if (g[f]) ges++;
            }
        }
        return new Zahl(w, c30, c80, ges, rahmen, luecken, ende);
    }

    private static string Text(string name, Zahl z, KorridorStand k) => string.Create(C,
        $"  C1N {name}: angeschnitten {z.Cut30}/{z.Woerter}, mehr als 80 ms {z.Cut80}, Lücken {z.Luecken}, Ende fehlt {z.EndeFehlt}, Rahmen gesendet {100.0 * z.Gesendet / z.Rahmen:0.0} %; Stimme {k.StimmeDb:0.0} Bezug {k.BezugDb:0.0} unten {k.UntergrenzeDb:0.0} oben {k.ObergrenzeDb:0.0}");

    /// <summary>Einen stillen Rahmen durch die Pegelstufe (übernimmt die Einstellung im Aufnahme-Thread).</summary>
    private static Pegelstufe Pegelstufe(LautstaerkeEinstellung e)
    {
        var p = new Pegelstufe(e);
        p.Verarbeiten(new float[R], 0f);
        return p;
    }

    /// <summary>
    /// H1: Ist die gemerkte Verstärkung an ihrer Grenze (+30 dB bei einem leisen Mikrofon), wäre Ziel minus Verstärkung um die Begrenzung zu laut;
    /// die Stimme gilt dann als unbekannt (die Halteregel lernt sie), oder sie kommt gemessen mit. Probe A: Mikrofon 35 dB leiser, eingemessen auf
    /// die begrenzten +30 dB, schneidet nicht wesentlich mehr an als das normale Mikrofon.
    /// </summary>
    [Test]
    public static async Task C1N_H1_Anker_AusGemessenerStimme_NichtAusBegrenzterVerstaerkung()
    {
        var begrenzt = Pegelstufe(new LautstaerkeEinstellung(true, Funk.Klang.Verarbeitung.Pegelstufe.MaxDb, Gelernt: true));
        Pruefe.Falsch(begrenzt.StimmeBekannt, "Verstärkung an der Grenze, keine gemessene Stimme: unbekannt");
        Pruefe.Wahr(float.IsNaN(begrenzt.AnkerDbfs), "kein Anker aus der Begrenzung");
        var gemessen = Pegelstufe(new LautstaerkeEinstellung(true, Funk.Klang.Verarbeitung.Pegelstufe.MaxDb, Gelernt: true, StimmeDbfs: -61f));
        Pruefe.Wahr(gemessen.StimmeBekannt, "gemessene Stimme mitgegeben: bekannt");
        PruefeWert.Nahe(-61.0, gemessen.AnkerDbfs, 0.01, "Anker ist die gemessene Stimme");
        var fest = Pegelstufe(new LautstaerkeEinstellung(false, Funk.Klang.Verarbeitung.Pegelstufe.MaxDb, Gelernt: true));
        Pruefe.Falsch(fest.StimmeBekannt, "fest an der Grenze ebenso");
        var normal = Pegelstufe(new LautstaerkeEinstellung(true, 3f, Gelernt: true));
        PruefeWert.Nahe(Funk.Klang.Verarbeitung.Pegelstufe.ZielDbfs - 3.0, normal.AnkerDbfs, 0.01, "nicht begrenzt: wie bisher Ziel minus Verstärkung");

        var zn = Zaehlen((await Lauf(new LautstaerkeEinstellung(true, 3f, Gelernt: true, Eingemessen: true), 15f, 0f, 77)).T);
        var leise = await Lauf(new LautstaerkeEinstellung(true, 30f, Gelernt: true, Eingemessen: true), 15f, -35f, 77);
        var zl = Zaehlen(leise.T);
        var leiseGemessen = await Lauf(new LautstaerkeEinstellung(true, 30f, Gelernt: true, Eingemessen: true, StimmeDbfs: -26f - 35f), 15f, -35f, 77);
        var zg = Zaehlen(leiseGemessen.T);
        Console.WriteLine(Text("H1 Mikrofon 0 dB, +3 eingemessen", zn, KorridorStand.Leer));
        Console.WriteLine(Text("H1 Mikrofon -35 dB, +30 eingemessen (begrenzt)", zl, leise.K));
        Console.WriteLine(Text("H1 Mikrofon -35 dB, +30 eingemessen, Stimme gemessen mitgegeben", zg, leiseGemessen.K));
        PruefeWert.Hoechstens(zn.Cut30 + 5, zl.Cut30, "leises Mikrofon (Verstärkung begrenzt) schneidet nicht wesentlich mehr an als normales");
        PruefeWert.Hoechstens(zn.Cut80 + 3, zl.Cut80, "mehr als 80 ms fehlend ebenso");
        PruefeWert.Hoechstens(zn.Cut30 + 5, zg.Cut30, "mit gemessener Stimme ebenso");
    }

    /// <summary>
    /// M1: Die Untergrenze liegt nie näher als <see cref="GateVorgaben.KorridorMindestBreiteDb"/> an der Obergrenze, auch wenn der Bezug (Anker) über
    /// der jetzt leiseren Stimme liegt. Probe B: in der Führung Stimme -33, Bezug -23, Abstand 9; in der Kette eingemessen, dann das Mikrofon 10 dB
    /// leiser, vier Durchläufe.
    /// </summary>
    [Test]
    public static async Task C1N_M1_Korridor_HaeltMindestbreite_AuchWennDieStimmeSinkt()
    {
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard with { MindestAbstandDb = 9f }, StimmeDb = -33f, BezugDb = -23f };
        var still = new float[R];
        for (var i = 0; i < 10; i++)
        {
            _ = f.Vorbereiten(-80f, 0f, false);
            f.Gate.Verarbeiten(still, 0f);
        }
        Console.WriteLine(string.Create(C, $"  C1N M1 Führung Stimme -33, Bezug -23, Abstand 9: unten {f.Korridor.UntergrenzeDb:0.0}, oben {f.Korridor.ObergrenzeDb:0.0}"));
        PruefeWert.Mindestens(f.Korridor.UntergrenzeDb + GateVorgaben.KorridorMindestBreiteDb - 1e-3, f.Korridor.ObergrenzeDb, "Führung: Untergrenze mindestens 3 dB unter der Obergrenze");

        // Der Abstand, den das Produkt beim Einmessen einstellt (normaler Satz und derselbe 12 dB leiser, Werkswerte), nicht von Hand gesetzt.
        var abstand = Mindesteinmessung.AbstandFuer(12.0);
        var l = await Lauf(new LautstaerkeEinstellung(true, 3f, Gelernt: true, Eingemessen: true), abstand, -10f, 77, wiederholungen: 4);
        var z = Zaehlen(l.T);
        var ohne = Zaehlen((await Lauf(new LautstaerkeEinstellung(true, 3f, Gelernt: true, Eingemessen: true), 60f, -10f, 77, wiederholungen: 4)).T);
        Console.WriteLine(Text(string.Create(C, $"M1 eingemessen +3, Abstand {abstand:0.0} (Einmessen), Mikrofon 10 dB leiser, 4. Durchlauf"), z, l.K));
        Console.WriteLine(Text("M1 ebenso ohne Untergrenze (Gegenprobe)", ohne, KorridorStand.Leer));
        PruefeWert.Mindestens(l.K.UntergrenzeDb + GateVorgaben.KorridorMindestBreiteDb - 1e-3, l.K.ObergrenzeDb, "Kette: Untergrenze mindestens 3 dB unter der Obergrenze");
        // Sarevocs Maßstab (03.10.2026, wie C1_Einmessen_...): höchstens 2 % der Anfänge mit mehr als 80 ms Verlust, keine Lücke, kein Ende; die
        // 30-ms-Zahl bleibt Messwert. Vorher 8 von 164 über 80 ms (der Anker folgte erst ab 6 dB Abweichung).
        PruefeWert.Hoechstens(3, z.Cut80, "die leisere Stimme: höchstens 3 von 164 Anfängen über 80 ms");
        Pruefe.Gleich(0, z.Luecken, "keine Lücke");
        Pruefe.Gleich(0, z.EndeFehlt, "kein Ende fehlt");
    }

    /// <summary>
    /// M4: Fest eingemessen mit dem Mikrofon, auf dem eingemessen wurde (<see cref="LautstaerkeEinstellung.Eingang"/>): Ein anderes, 15 dB leiseres
    /// Mikrofon beim nächsten Start bekommt den alten Anker nicht, die Stimme gilt als unbekannt, und die Sprachaktivierung sendet weiter
    /// (Probe C: ohne Gerätebezug 68,8 % der Rahmen, ohne Untergrenze 97,3 %). Seit C2a zählt das doppelt: Mit dem alten Anker hielte der Wächter
    /// die 15 dB leisere Stimme für leises Reden im Raum (ohne Gerätebezug, Untergrenze und Wächter an: 19 % der Rahmen).
    /// </summary>
    [Test]
    public static async Task C1N_M4_FestEingemessen_AnderesMikrofon_GiltNichtAlsBekannt()
    {
        var anderes = await Lauf(new LautstaerkeEinstellung(false, 3f, Gelernt: true, Eingang: "das-mikrofon-beim-einmessen"), 15f, -15f, 77);
        var ohne = await Lauf(new LautstaerkeEinstellung(false, 3f, Gelernt: true), 60f, -15f, 77, waechter: false);
        var za = Zaehlen(anderes.T);
        var zo = Zaehlen(ohne.T);
        Console.WriteLine(Text($"M4 fest +3 eingemessen auf einem anderen Mikrofon ({anderes.Eingang}), 15 dB leiser, Abstand 15", za, anderes.K));
        Console.WriteLine(Text("M4 ebenso ohne Gerätebezug, ohne Untergrenze und ohne Wächter (Vergleich)", zo, ohne.K));
        Pruefe.Falsch(anderes.K.StimmeBekannt, "anderes Mikrofon: die eingemessene Stimme gilt nicht");
        PruefeWert.Mindestens(0.95 * zo.Gesendet, za.Gesendet, "die Sprachaktivierung sendet weiter");
        var gleich = await Lauf(new LautstaerkeEinstellung(false, 3f, Gelernt: true, Eingang: anderes.Eingang), 15f, 0f, 77);
        Pruefe.Wahr(gleich.K.StimmeBekannt, "dasselbe Mikrofon: eingemessen gilt");
    }
}
