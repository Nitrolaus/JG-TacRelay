// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Nacharbeit nach der Gegenprüfung der Klangstufe C2a (output\test\20261004_pruefung-c2a.md), Vorlage sind die Proben P1 bis P7 des Prüfers
/// (tmp\tr-pruef-c2a\PruefC2aProben.cs). Sarevocs harte Vorgaben: "Hit muss raus, im Zweifel 80 ms, nie abschneiden, nie unterbrechen".
/// </summary>
internal static class KlangC2FixTests
{
    private const int R = 480;
    private const int Rate = 48000;
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static float[] MitRauschen(float[] x, ulong saat)
    {
        var r = AnbindungSignale.Rauschen(x.Length, Pumpmessung.GrundrauschenDbfs, saat);
        for (var i = 0; i < x.Length; i++) r[i] += x[i];
        return r;
    }

    // ---------------------------------------------------------------------------------------------------------------------------- H1

    /// <summary>
    /// H1: Nach dem Echo-Urteil ohne Echounterdrückung gehen alle eigenen Sprüche hinaus, auch mit RNNoise, das ihren Anlaut im Echo-Gemisch um 15
    /// bis 40 dB drückt: Das Funkniveau vor dem Filter öffnet allgemein, nicht nur während der Raum-Anhebung (vorher 8 von 10).
    /// </summary>
    [Test]
    public static async Task C2F_H1_NachEchoUrteil_MitRnnoise_AlleEigenenSpruecheHinaus()
    {
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = false;
        await s.StartenAsync();
        var moritz = KlangC2EmpfangTests.Korpusrede(150, out var phasen, saat: 41, pausenMs: 2000);
        s.Maschine.Mischer.QuelleHinzufuegen(new Funkquelle(moritz, phasen), 0);
        KlangC2MaschineTests.EchoAnlegen(s, x => 0.25f * x, verzugMs: 80);
        var mikrofon = AnbindungSignale.Rauschen(150 * Rate, Pumpmessung.GrundrauschenDbfs, 7);
        var eigen = KlangC2EmpfangTests.Korpusrede(70, out var eigenePhasen, saat: 42, pausenMs: 3500, nurFunklaut: true);
        for (var i = 0; i < eigen.Length; i++) mikrofon[(80 * Rate) + i] += eigen[i];
        long erkannt = -1;
        s.Abspielen(mikrofon, t =>
        {
            if (erkannt < 0 && s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Echo) erkannt = t;
        });
        var hinaus = eigenePhasen.Count(p => s.Pakete.Any(k => (k.Kennzeichen & Sprachkennzeichen.Beginn) != 0
            && k.Takt >= 8000 + (p.Von / R) - 20 && k.Takt <= 8000 + (p.Von / R) + 60));
        Console.WriteLine($"  C2F H1: Echo erkannt nach {erkannt / 100.0:0.0} s; eigene Sprüche ab 80 s {hinaus} von {eigenePhasen.Count}");
        Pruefe.Wahr(erkannt is > 0 and < 8000, "Echo vor den eigenen Sprüchen erkannt");
        Pruefe.Gleich(eigenePhasen.Count, hinaus, "alle eigenen Sprüche gehen hinaus, auch mitten in Moritz' Reden");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- H2

    /// <summary>
    /// H2 (Entscheidung der Koordination): Ab Werk ist "Leises Reden im Raum aussperren" aus. Fernseher im Raum (-18 dB), die eigene Stimme 3 und
    /// 7 dB unter dem gemerkten Schnitt (müde): 0 Wortanfänge über 80 ms, 0 Lücken, 0 Enden, wie ohne Wächter. Mit Schalter an als Messwert
    /// (d1bb2d9: 5 und 7 von 55 über 80 ms).
    /// </summary>
    [Test]
    public static async Task C2F_H2_Raumgeraeusch_EigeneStimmeLeiser_AbWerkKeinVerlust()
    {
        var ergebnisse = new List<(float Db, MessungKlangC2.Saetze AbWerk, MessungKlangC2.Saetze MitSchalter)>();
        foreach (var db in new[] { -3f, -7f })
        {
            MessungKlangC2.Plan Plan(bool sperren)
            {
                var p = MessungKlangC2.Raum("C2F H2 Fernseher", 150, 7102, -18f, 0.95f, 0.3, 30);
                p.LeisesRedenSperren = sperren;
                var g = Pegel.DbZuLinear(db);
                for (var i = 0; i < p.Eigen.Count; i++) p.Eigen[i] = p.Eigen[i] with { Gain = p.Eigen[i].Gain * g };
                return p;
            }
            MessungKlangC2.Saetze Zaehlen(MessungKlangC2.Ergebnis e, MessungKlangC2.Plan p) =>
                MessungKlangC2.Saetze.Aus(e, p.Eigen.Where(t => t.Start >= 45 * Rate && !t.Kurz));
            var pw = Plan(false);
            var ps = Plan(true);
            var w = MessungKlangC2.Laufen(pw, true);
            var m = MessungKlangC2.Laufen(ps, true);
            ergebnisse.Add((db, Zaehlen(await w, pw), Zaehlen(await m, ps)));
        }
        foreach (var (db, abWerk, mit) in ergebnisse)
        {
            Console.WriteLine(string.Create(C, $"  C2F H2 Fernseher -18 dB, eigene Stimme {db:0} dB: ab Werk {abWerk}; mit \"Leises Reden aussperren\" {mit}"));
        }
        foreach (var (db, abWerk, _) in ergebnisse)
        {
            Pruefe.Gleich(0, abWerk.Ueber80, string.Create(C, $"{db:0} dB ab Werk: kein Wortanfang über 80 ms"));
            Pruefe.Gleich(0, abWerk.Luecken, string.Create(C, $"{db:0} dB ab Werk: keine Lücke"));
            Pruefe.Gleich(0, abWerk.EndeFehlt, string.Create(C, $"{db:0} dB ab Werk: kein Ende fehlt"));
        }
    }

    /// <summary>Der Schalter "Leises Reden im Raum aussperren" ist ab Werk aus und wirkt: aus kein Raumgeräusch, an wird leise Rede gesperrt.</summary>
    [Test]
    public static async Task C2F_LeisesRedenSperren_AbWerkAus_SchalterWirkt()
    {
        Pruefe.Falsch(DaempfungEinstellung.AbWerk.LeisesRedenSperren, "Klangkern ab Werk aus");
        Pruefe.Falsch(new Funk.Client.Ablage.DaempfungWerte().AlsEinstellung().LeisesRedenSperren, "Einstellungsdatei ab Werk aus");
        var ergebnis = new Dictionary<bool, (bool Raum, int Leise)>();
        foreach (var sperren in new[] { false, true })
        {
            await using var s = new Pruefstand();
            s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
            s.Maschine.Echounterdrueckung = false;
            s.Maschine.Daempfung = DaempfungEinstellung.AbWerk with { LeisesRedenSperren = sperren };
            await s.StartenAsync();
            s.Abspielen(KlangC2EmpfangTests.Korpusrede(15, out _, saat: 11, pausenMs: 1500, nurFunklaut: true));
            var ab = s.Takte;
            var raum = false;
            s.Abspielen(KlangC2EmpfangTests.Korpusrede(40, out _, saat: 12, pausenMs: 1500, pegelDb: -12f, nurGleichmaessig: true),
                _ => raum |= s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Raumgeraeusch);
            var leise = s.Pakete.Count(k => k.Takt >= ab && (k.Kennzeichen & Sprachkennzeichen.Beginn) != 0);
            ergebnis[sperren] = (raum, leise);
        }
        Console.WriteLine($"  C2F Schalter leises Reden: aus Raumgeräusch {ergebnis[false].Raum}, leise Phasen hinaus {ergebnis[false].Leise}; an {ergebnis[true].Raum}, {ergebnis[true].Leise}");
        Pruefe.Falsch(ergebnis[false].Raum, "aus: kein Raumgeräusch");
        Pruefe.Wahr(ergebnis[true].Raum, "an: Raumgeräusch");
        PruefeWert.Hoechstens(ergebnis[false].Leise - 3, ergebnis[true].Leise, "an: deutlich weniger leise Phasen hinaus");
    }

    /// <summary>H2: Eine offene Phase schließt die Raum-Anhebung nie: Solange das Gate offen ist, gilt die normale Linie (Nachlauf und Halten).</summary>
    [Test]
    public static void C2F_H2_OffenePhase_LinieOhneAnhebung()
    {
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = -23f, BezugDb = -23f, ZusatzWunschDb = -26f };
        var still = new float[R];
        for (var i = 0; i < 150; i++)
        {
            _ = f.Vorbereiten(-80f, 0f, false);
            f.Gate.Verarbeiten(still, 0f);
        }
        var anhebung = f.LinieDb;
        // Ein Satz auf -24 dBFS öffnet über den normalen Weg (Anlauf, Bestätigung, Freigabe).
        var a = MathF.Sqrt(2f) * MathF.Pow(10f, -24f / 20f);
        var ton = new float[R];
        for (var i = 0; i < R; i++) ton[i] = a * MathF.Sin(2f * MathF.PI * 200f * i / Rate);
        for (var r = 0; r < 10; r++)
        {
            var p = f.Vorbereiten(-24f, 0.95f, false);
            f.Gate.Verarbeiten(ton, p);
        }
        Pruefe.Wahr(f.Offen, "offen");
        _ = f.Vorbereiten(-40f, 0.95f, false);
        Console.WriteLine(string.Create(C, $"  C2F H2 Linie: geschlossen {anhebung:0.0}, in der offenen Phase {f.LinieDb:0.0}"));
        PruefeWert.Hoechstens(anhebung - 10.0, f.LinieDb, "in der offenen Phase die normale Linie");
        PruefeWert.Hoechstens(-29.0 + 1e-3, f.Gate.FunkniveauDbfs, "Funkniveau höchstens Stimme minus 6 dB, nicht Stimme minus 2 dB");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- M1

    /// <summary>M1: Die klemmende Taste schaltet erst in der nächsten Pause ab, nicht mitten im Satz (Probe P2).</summary>
    [Test]
    public static async Task C2F_M1_KlemmendeTaste_AbschaltenErstInDerPause()
    {
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = false;
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        s.Maschine.SprechtasteGedrueckt();
        var druck = s.Takte;
        s.Abspielen(AnbindungSignale.Rauschen(52 * Rate, Pumpmessung.GrundrauschenDbfs, 31));
        var rede = KlangC2EmpfangTests.Korpusrede(20, out var ph, saat: 32, pausenMs: 300, nurFunklaut: true);
        var redeAb = s.Takte;
        s.Abspielen(MitRauschen(rede, 33));
        // Danach schweigt der Funker, die Taste klemmt weiter: In dieser Pause schaltet sie ab.
        var redeEnde = redeAb + (ph[^1].Bis / R);
        s.Abspielen(AnbindungSignale.Rauschen(2 * Rate, Pumpmessung.GrundrauschenDbfs, 34));
        var gesperrt = s.Maschine.Kette.TasteGesperrt;
        s.Maschine.SprechtasteLosgelassen();
        var letzte = s.Pakete.Where(p => p.Opus.Length > 0).Max(p => p.Takt);
        var imSatz = ph.Any(p => redeAb + (p.Von / R) + 5 <= letzte && redeAb + (p.Bis / R) - 5 > letzte);
        Console.WriteLine(string.Create(C, $"  C2F M1: letztes Paket nach {(letzte - druck) / 100.0:0.00} s (Rede bis {(redeEnde - druck) / 100.0:0.00} s), mitten im Satz {imSatz}, gesperrt {gesperrt}"));
        PruefeWert.Mindestens(druck + 6000, letzte, "frühestens nach 60 s");
        Pruefe.Falsch(imSatz, "nicht mitten im Satz abgeschaltet");
        Pruefe.Wahr(gesperrt, "in der Pause abgeschaltet");
        PruefeWert.Hoechstens(redeEnde + 80, letzte, "spätestens 800 ms nach dem letzten Satz");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- M2

    /// <summary>M2: Nach dem Lösen von "Mikro stumm" geht nichts von vor dem Stummschalten hinaus (Probe P1: 6 von 7 PREROLL-Paketen trugen den alten Ton).</summary>
    [Test]
    public static async Task C2F_M2_MikroStumm_NachDemLoesenKeinAlterTon()
    {
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = false;
        s.Maschine.Rauschfilter = Rauschfilter.Aus;
        await s.StartenAsync();
        s.Abspielen(MitRauschen(KlangC2EmpfangTests.Korpusrede(8, out _, saat: 5, pausenMs: 700, nurFunklaut: true), 21));
        s.Abspielen(AnbindungSignale.Rauschen(2 * Rate, Pumpmessung.GrundrauschenDbfs, 22));
        var m = AnbindungSignale.Rauschen(30 * R, Pumpmessung.GrundrauschenDbfs, 23);
        for (var i = 0; i < m.Length; i++) m[i] += 0.1f * MathF.Sin(2f * MathF.PI * 1000f * i / Rate);
        s.Abspielen(m);
        s.Maschine.MikroStumm = true;
        s.Abspielen(AnbindungSignale.Rauschen(3 * Rate, Pumpmessung.GrundrauschenDbfs, 24));
        s.Maschine.MikroStumm = false;
        var ab = s.Takte;
        var nachher = KlangC2EmpfangTests.Korpusrede(4, out var ph, saat: 6, pausenMs: 500, nurFunklaut: true);
        s.Abspielen(MitRauschen(nachher.AsSpan(ph[0].Von).ToArray(), 25));
        s.Stille(50);
        using var dek = new OpusDekodierer();
        var puffer = new float[OpusDekodierer.MaxSamples];
        var hoechsterTon = 0.0;
        var pakete = 0;
        foreach (var p in s.Pakete.Where(p => p.Takt > ab))
        {
            if (p.Opus.Length == 0) continue;
            pakete++;
            var n = dek.Dekodieren(p.Opus, puffer);
            double summe = 0, re = 0, im = 0;
            for (var i = 0; i < n; i++)
            {
                summe += puffer[i] * (double)puffer[i];
                var w = 2 * Math.PI * 1000.0 * i / Rate;
                re += puffer[i] * Math.Cos(w);
                im -= puffer[i] * Math.Sin(w);
            }
            var db = 10 * Math.Log10(Math.Max(1e-12, summe / Math.Max(1, n)));
            if (db > -40) hoechsterTon = Math.Max(hoechsterTon, summe > 0 ? 2 * ((re * re) + (im * im)) / (n * summe) : 0);
            if (pakete >= 8) break;
        }
        Console.WriteLine(string.Create(C, $"  C2F M2: Pakete nach dem Lösen {pakete}, höchster Tonanteil (ab -40 dBFS) {hoechsterTon:0.00}"));
        Pruefe.Wahr(pakete > 0, "nach dem Lösen wird gesendet");
        PruefeWert.Hoechstens(0.3, hoechsterTon, "kein Ton von vor dem Stummschalten im Vorpuffer");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- M3

    private sealed class Ton(float amp, bool aktiv) : IKlangquelle
    {
        private long _n;
        private bool _erst = true;

        public QuellenEreignis Lesen(Span<float> ziel)
        {
            for (var i = 0; i < ziel.Length; i++) ziel[i] = aktiv ? amp * MathF.Sin(2f * MathF.PI * 440f * (_n + i) / Rate) : 0f;
            _n += ziel.Length;
            var e = QuellenEreignis.Aktiv;
            if (_erst) e |= QuellenEreignis.Begonnen;
            _erst = false;
            return e;
        }
    }

    private static double MischPegel(float absenkung, bool durchsage, float gesamt)
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1, AbsenkungDb = absenkung }) { Klick = null };
        _ = m.QuelleHinzufuegen(new Ton(0.1f, true), 0);
        if (durchsage) _ = m.QuelleHinzufuegen(new Ton(0f, false), 1, durchsage: true);
        m.FunkGesamtDb = gesamt;
        var aus = new float[R];
        double summe = 0;
        var n = 0;
        for (var b = 0; b < 800; b++)
        {
            m.Mischen(aus, 1);
            if (b < 700) continue;
            foreach (var v in aus) summe += v * (double)v;
            n += R;
        }
        return 10 * Math.Log10(summe / n);
    }

    /// <summary>M3: Die Gesamtabsenkung des Wächters zählt wie jede andere: stärkste Absenkung, nicht addiert, nie unter die gemeinsame Untergrenze (Probe P3).</summary>
    [Test]
    public static void C2F_M3_Mischer_GesamtabsenkungNichtAddiert_GemeinsameUntergrenze()
    {
        var bezug = MischPegel(-12f, false, 0f);
        var d12g = MischPegel(-12f, true, -6f) - bezug;
        var d30g = MischPegel(-30f, true, -6f) - bezug;
        var g6 = MischPegel(-12f, false, -6f) - bezug;
        Console.WriteLine(string.Create(C, $"  C2F M3: Gesamt -6: {g6:0.0} dB; Durchsage -12 und Gesamt -6: {d12g:0.0} dB; Durchsage -30 und Gesamt -6: {d30g:0.0} dB"));
        PruefeWert.Nahe(-6.0, g6, 0.5, "Gesamt allein: -6");
        PruefeWert.Nahe(-12.0, d12g, 0.5, "Durchsage -12 und Gesamt -6: die stärkere, -12");
        PruefeWert.Mindestens(Mischer.GemeinsameUntergrenzeDb - 0.5, d30g, "Durchsage -30 und Gesamt -6: nie unter die gemeinsame Untergrenze");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- M4

    /// <summary>
    /// M4: Schalter aus nach dem Echo-Urteil nimmt den Funkspur-Schutz zurück (Probe P7: 0 statt 33 Echo-Phasen); mit Schalter an hält das Urteil,
    /// solange das Echo da ist (vorher nach 1 bis 2 min entwarnt); ein Wechsel des Mikrofons setzt den Wächter zurück.
    /// </summary>
    [Test]
    public static async Task C2F_M4_FunkspurSchutz_SchalterAus_UrteilHaelt_Zuruecksetzen()
    {
        var ergebnis = new Dictionary<bool, (DauersendUrteil Bei200, int Danach, bool Schutz)>();
        foreach (var ausschalten in new[] { true, false })
        {
            await using var s = new Pruefstand();
            s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
            s.Maschine.Echounterdrueckung = false;
            await s.StartenAsync();
            var moritz = KlangC2EmpfangTests.Korpusrede(200, out var phasen, saat: 41, pausenMs: 2000);
            _ = s.Maschine.Mischer.QuelleHinzufuegen(new Funkquelle(moritz, phasen), 0);
            KlangC2MaschineTests.EchoAnlegen(s, x => 0.25f * x, verzugMs: 80);
            var mikrofon = AnbindungSignale.Rauschen(200 * Rate, Pumpmessung.GrundrauschenDbfs, 7);
            s.Abspielen(mikrofon.AsSpan(0, 100 * Rate));
            Pruefe.Gleich(DauersendUrteil.Echo, s.Maschine.EigenesUrteil.Urteil, "Echo erkannt");
            if (ausschalten) s.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser);
            var ab = s.Takte;
            s.Abspielen(mikrofon.AsSpan(100 * Rate, 100 * Rate));
            var danach = s.Pakete.Count(p => p.Takt >= ab + 200 && (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0);
            ergebnis[ausschalten] = (s.Maschine.EigenesUrteil.Urteil, danach, s.Maschine.Kette.FunkspurSchutz);
            if (!ausschalten)
            {
                s.Maschine.Kette.WaechterZuruecksetzen();
                s.Stille(2);
                Pruefe.Falsch(s.Maschine.Kette.FunkspurSchutz, "Zurücksetzen (anderes Mikrofon): kein Funkspur-Schutz mehr");
                Pruefe.Gleich(DauersendUrteil.Keins, s.Maschine.EigenesUrteil.Urteil, "Zurücksetzen: kein Urteil");
            }
        }
        Console.WriteLine($"  C2F M4: Schalter aus ab 100 s: Echo-Phasen danach {ergebnis[true].Danach}, Schutz {ergebnis[true].Schutz}; Schalter an: Urteil bei 200 s {ergebnis[false].Bei200}, Echo-Phasen danach {ergebnis[false].Danach}");
        Pruefe.Falsch(ergebnis[true].Schutz, "Schalter aus: kein Funkspur-Schutz");
        PruefeWert.Mindestens(10, ergebnis[true].Danach, "Schalter aus: das Echo geht wie ohne Wächter hinaus");
        Pruefe.Gleich(DauersendUrteil.Echo, ergebnis[false].Bei200, "Schalter an: das Urteil hält, solange das Echo da ist");
        PruefeWert.Hoechstens(1, ergebnis[false].Danach, "Schalter an: kein Echo hinaus");
    }

    // ---------------------------------------------------------------------------------------------------------------------------- N1

    /// <summary>
    /// N1: Schalter aus nimmt die Angleichung gerampt zurück, nicht in einem Block. Seit 0.9.0 (Q48) ist das der eigene Schalter Pegelausgleich;
    /// bis 0.8.x war es der Schalter der Auto Dauersend-Dämpfung.
    /// </summary>
    [Test]
    public static void C2F_N1_Angleichung_SchalterAus_Gerampt()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var laut = KlangC2EmpfangTests.Korpusrede(150, out var phasen, saat: 8, pausenMs: 1500, pegelDb: 16f);
        var p = m.QuelleHinzufuegen(new Funkquelle(laut, phasen), 0);
        var block = new float[R];
        for (var b = 0; b < 120 * 100; b++) m.Mischen(block, 1);
        var vorher = m.Zustand(p).AngleichungDb;
        m.Waechter.AngleichungAn = false;
        for (var b = 0; b < 3; b++) m.Mischen(block, 1);
        var gleichDanach = m.Zustand(p).AngleichungDb;
        Console.WriteLine(string.Create(C, $"  C2F N1: Angleichung vorher {vorher:0.0} dB, einen Block nach dem Ausschalten {gleichDanach:0.0} dB"));
        PruefeWert.Hoechstens(-5.0, vorher, "angeglichen");
        PruefeWert.Hoechstens(vorher + 0.5, gleichDanach, "nicht in einem Block zurück");
        for (var b = 0; b < 10 * 100; b++) m.Mischen(block, 1);
        Pruefe.Gleich(0f, m.Zustand(p).AngleichungDb, "nach einigen Sekunden zurück");
    }
}
