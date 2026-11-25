// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>Jitterpuffer gegen simulierte Ankunftsspuren; alle Zeiten simuliert, alle Zufallswerte mit festem Seed.</summary>
internal static class JitterpufferTests
{
    private const long Sekunde = 1_000_000;

    /// <summary>Vier Sprechphasen zu 5 s mit 1 s Pause, Frame-Nr an der Sitzungsuhr.</summary>
    private static List<SimPaket> VierPhasen(Func<uint, long?> netz, bool va = false)
    {
        var alle = new List<SimPaket>();
        for (var i = 0; i < 4; i++)
            alle.AddRange(Netzsimulation.Phase((ushort)(10 + i), (uint)(i * 300), 250, netz, va));
        return alle;
    }

    private static void Bericht(string szene, Wiedergabeergebnis e, Jitterpuffer jp, AttrappenDekodierer dek)
    {
        var s = jp.Statistik;
        var spaet = e.Frames.Select(f => f.VerzoegerungMs).Order().ToList();
        var p95 = spaet.Count == 0 ? 0 : spaet[(int)(spaet.Count * 0.95)];
        var mittel = spaet.Count == 0 ? 0 : spaet.Average();
        Console.WriteLine(FormattableString.Invariant(
            $"  Jitterpuffer {szene}: Ziel {s.ZielMs:F0} ms, Verzögerung ab Senden Mittel {mittel:F0} ms / p95 {p95:F0} ms, FEC {dek.Fec}, PLC {dek.PlcSamples / 960.0:F1} Frames, Unterläufe {s.Unterlaeufe}, gekürzt {s.GekuerztSamples / 48.0:F0} ms, gedehnt {s.GedehntSamples / 48.0:F0} ms, Jitter RFC 3550 {s.JitterMs:F1} ms"));
    }

    [Test]
    public static void Gleichmaessig_KeineVerdeckung_ZielSinktAmSprechanfang()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var e = Netzsimulation.Abspielen(jp, uhr, VierPhasen(Netzsimulation.Konstant(30_000)), 25 * Sekunde);
        var s = jp.Statistik;
        Bericht("gleichmäßig 30 ms", e, jp, dek);

        Pruefe.Gleich(1000, dek.Normal, "alle Frames normal dekodiert");
        Pruefe.Gleich(0, dek.Fec + dek.Plc, "keine Verdeckung");
        Pruefe.Gleich(0L, s.Unterlaeufe, "keine Unterläufe");
        Pruefe.Gleich(4L, s.Phasen, "vier Phasen");

        // Erste Phase mit Startwert 80 ms, danach aus der Statistik (kein Jitter): Minimum mit FEC 40 ms.
        var erste = e.Frames.Where(f => f.Frame < 250).Average(f => f.VerzoegerungMs);
        var vierte = e.Frames.Where(f => f.Frame >= 900).Average(f => f.VerzoegerungMs);
        // Gemessen ab Sendezeit: Netz + Ziel - 20 ms (der gerade dekodierte Frame zählt zum Füllstand) + Abholraster.
        PruefeWert.Nahe(30 + 80 - 15, erste, 10, "Verzögerung erste Phase (Netz 30 ms, Start 80 ms)");
        PruefeWert.Nahe(30 + 40 - 15, vierte, 10, "Verzögerung vierte Phase (Netz 30 ms, Minimum 40 ms)");
        Pruefe.Gleich(40.0, s.ZielMs, "Ziel nach Anpassung");

        // Innerhalb einer Phase bleibt die Verzögerung konstant (keine Anpassung mitten im Satz nach unten).
        var ersteMin = e.Frames.Where(f => f.Frame < 250).Min(f => f.VerzoegerungMs);
        var ersteMax = e.Frames.Where(f => f.Frame < 250).Max(f => f.VerzoegerungMs);
        PruefeWert.Hoechstens(10, ersteMax - ersteMin, "Schwankung innerhalb der ersten Phase");
        Pruefe.Falsch(e.NichtEndlich, "nur endliche Werte");
        PruefeWert.Hoechstens(1.0, e.Spitze, "Ausgabe nie über 1,0");
    }

    /// <summary>Erste Phase (mit Startwert 80 ms) und die übrigen drei getrennt abspielen, um sie einzeln zu zählen.</summary>
    private static (JitterpufferStatistik NachPhase1, Wiedergabeergebnis Danach) PhaseEinsUndRest(Jitterpuffer jp, SimUhr uhr, List<SimPaket> alle)
    {
        Netzsimulation.Abspielen(jp, uhr, alle.Where(p => p.Frame < 300), 5_800_000);
        var nachPhase1 = jp.Statistik;
        var rest = Netzsimulation.Abspielen(jp, uhr, alle.Where(p => p.Frame >= 300), 25 * Sekunde, startUs: 5_803_000);
        return (nachPhase1, rest);
    }

    [Test]
    public static void Jitter_Quantil95_KeineUnterlaeufeNachDerErstenPhase()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var (phase1, e) = PhaseEinsUndRest(jp, uhr, VierPhasen(Netzsimulation.Jitter(30_000, 60_000, 7)));
        var s = jp.Statistik;
        Bericht("Jitter 30-90 ms", e, jp, dek);

        // Ziel: 20 ms + Quantil der relativen Verzögerung (0..60 ms), also rund 80 ms.
        PruefeWert.Nahe(80, s.ZielMs, 10, "Ziel bei 60 ms Jitter");
        Pruefe.Gleich(phase1.Unterlaeufe, s.Unterlaeufe, "keine Unterläufe nach der ersten Phase");
        Pruefe.Gleich(phase1.Plc + phase1.Fec, s.Plc + s.Fec, "keine Verdeckung nach der ersten Phase");
        PruefeWert.Hoechstens(0.05, (dek.Fec + dek.Plc) / 1000.0, "Anteil verdeckter Frames insgesamt");
        // Nötig sind 90 ms (höchste Laufzeit); darüber nur Eimerbreite, Intervall-Höchstwert und Abholraster.
        var verzoegerung = e.Frames.Average(f => f.VerzoegerungMs);
        PruefeWert.Hoechstens(90 + 20, verzoegerung, "Verzögerung ab Phase 2");
        PruefeWert.Hoechstens(1.0, e.Spitze, "Ausgabe nie über 1,0");
        Pruefe.Falsch(e.NichtEndlich, "nur endliche Werte");
    }

    [Test]
    public static void Buendel_ZielWaechst_NachDerErstenPhaseOhneUnterlauf()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        // Pakete stauen sich und kommen alle 100 ms gemeinsam: Laufzeit 40 bis 120 ms, relative Verzögerung bis 80 ms.
        var (phase1, e) = PhaseEinsUndRest(jp, uhr, VierPhasen(Netzsimulation.Buendel(30_000, 100_000)));
        var s = jp.Statistik;
        Bericht("Bündel alle 100 ms", e, jp, dek);

        PruefeWert.Nahe(105, s.ZielMs, 10, "Ziel bei 100-ms-Bündeln");
        PruefeWert.Mindestens(1, phase1.Unterlaeufe, "erste Phase mit Startwert 80 ms reicht nicht");
        Pruefe.Gleich(phase1.Unterlaeufe, s.Unterlaeufe, "danach keine Unterläufe");
        Pruefe.Gleich(phase1.Plc + phase1.Fec, s.Plc + s.Fec, "danach keine Verdeckung");
        var verzoegerung = e.Frames.Average(f => f.VerzoegerungMs);
        PruefeWert.Hoechstens(120 + 20, verzoegerung, "Verzögerung ab Phase 2 (nötig 120 ms)");
    }
    [Test]
    public static void Verlust_Einzeln_ImmerFec()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var netz = Netzsimulation.Verlust(Netzsimulation.Konstant(30_000), f => f % 10 == 9 && f < 245);
        Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 250, netz), 7 * Sekunde);

        Pruefe.Gleich(24, dek.Fec, "jeder Einzelverlust über FEC aus dem Folgepaket");
        Pruefe.Gleich(0, dek.Plc, "kein PLC");
        Pruefe.Gleich(226, dek.Normal, "übrige Frames normal");
        Pruefe.Gleich(0L, jp.Statistik.Unterlaeufe, "keine Unterläufe");
    }

    [Test]
    public static void Verlust_SerieDrei_PlcDannFec()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var netz = Netzsimulation.Verlust(Netzsimulation.Konstant(30_000), f => f is >= 50 and <= 52);
        Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 250, netz), 7 * Sekunde);

        // Puffer 80 ms, Frame 53 ist beim Abspielen von 50 schon da: 50 und 51 per PLC, 52 per FEC aus 53.
        Pruefe.Gleich(2 * 960, dek.PlcSamples, "zwei Frames PLC");
        Pruefe.Gleich(1, dek.Fec, "ein Frame FEC");
        Pruefe.Gleich(247, dek.Normal, "normale Frames");
        Pruefe.Gleich(0L, jp.Statistik.Unterlaeufe, "kein Unterlauf");
    }

    [Test]
    public static void Verlust_LangeSerie_HoechstensSechzigMsHoerbar_DannStilleUndEinblenden()
    {
        var dek = new AttrappenDekodierer { PlcAlsGleichwert = true };
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        // 200 ms Lücke (Frames 100-109), länger als der Puffer.
        var netz = Netzsimulation.Verlust(Netzsimulation.Konstant(30_000), f => f is >= 100 and <= 109);
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 250, netz), 7 * Sekunde, ausgabeMitschreiben: true);
        var s = jp.Statistik;
        var a = e.Ausgabe;

        // Verdeckung erscheint als Gleichwert 0,25 mit Ausblendung; danach exakte Stille.
        var nullAb = -1;
        var lauf = 0;
        for (var i = 48_000; i < a.Count && nullAb < 0; i++)
        {
            lauf = a[i] == 0f ? lauf + 1 : 0;
            if (lauf == 480) nullAb = i - 479;
        }
        Pruefe.Wahr(nullAb > 0, "Stille nach der Verdeckung");
        var verdeckt = 0;
        for (var i = nullAb - 1; i > 0 && a[i] > 0f && a[i] <= 0.25f && a[i] <= a[i - 1] + 1e-7f; i--) verdeckt++;
        PruefeWert.Hoechstens(60 * 48 + 120, verdeckt, "hörbare Verdeckung am Stück");
        PruefeWert.Mindestens(60 * 48 - 480, verdeckt, "Verdeckung nutzt die 60 ms");
        PruefeWert.Hoechstens(1e-5, a[nullAb - 1], "Ausblendung endet stetig");

        var stilleEnde = nullAb;
        while (stilleEnde < a.Count && a[stilleEnde] == 0f) stilleEnde++;
        PruefeWert.Hoechstens(0.01, MathF.Abs(a[stilleEnde + 1]), "Einblenden danach mit Steigung null");
        Pruefe.Gleich(240, dek.Normal, "alle übrigen Frames normal");
        PruefeWert.Mindestens(1, s.Unterlaeufe, "Lücke als Unterlauf erkannt");
        PruefeWert.Mindestens(480, s.StilleSamples, "Stille gezählt");
        PruefeWert.Hoechstens(1.0, e.Spitze, "Ausgabe nie über 1,0");
    }
    [Test]
    public static void Umordnung_Paarweise_OhneVerlust()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        // Gerade Frames kommen 25 ms später, also nach dem folgenden ungeraden.
        Func<uint, long?> netz = f => f % 2 == 0 ? 55_000 : 30_000;
        Netzsimulation.Abspielen(jp, uhr, VierPhasen(netz), 25 * Sekunde);
        var s = jp.Statistik;

        Pruefe.Gleich(1000, dek.Normal, "alle Frames trotz Umordnung normal");
        Pruefe.Gleich(0, dek.Fec + dek.Plc, "keine Verdeckung");
        Pruefe.Gleich(0L, s.SpaetVerworfen, "nichts zu spät");
        PruefeWert.Mindestens(40, s.ZielMs, "Ziel deckt die Umordnung");
    }

    [Test]
    public static void Spaet_WirdVerworfen_UndPerFecErsetzt()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        Func<uint, long?> netz = f => f == 100 ? 600_000 : 30_000;
        Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 250, netz), 7 * Sekunde);
        var s = jp.Statistik;

        Pruefe.Gleich(1L, s.SpaetVerworfen, "verspätetes Paket verworfen");
        Pruefe.Gleich(1, dek.Fec, "Lücke per FEC geschlossen");
        Pruefe.Gleich(249, dek.Normal, "übrige normal");
    }

    [Test]
    public static void Preroll_BurstWirdBinnenEinerSekundeAbgebaut()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        // 7 Vorlauf-Frames (140 ms) kommen als Burst mit dem ersten Echtzeit-Frame, danach Echtzeit.
        var pakete = Netzsimulation.Phase(1, 0, 150, Netzsimulation.Konstant(30_000), va: true, vorlauf: 7);
        var e = Netzsimulation.Abspielen(jp, uhr, pakete, 4 * Sekunde);
        var s = jp.Statistik;

        var start = e.Frames[0].ZeitUs;
        var anfang = e.Frames[0].VerzoegerungMs;
        var spaeter = e.Ab(start + Sekunde).Where(f => f.Frame < 145).Max(f => f.VerzoegerungMs);
        PruefeWert.Mindestens(150, anfang, "Mehrverzögerung zu Beginn (Burst)");
        // Nach 1 s höchstens Ziel (80 ms) plus Toleranz von einem Frame über der Netzlaufzeit.
        PruefeWert.Hoechstens(30 + 80 + 20, spaeter, "Verzögerung nach 1 s");
        PruefeWert.Mindestens(40 * 48, s.GekuerztSamples, "gekürzte Samples");
        Pruefe.Gleich(150, dek.Normal, "kein Frame verloren");
        Pruefe.Gleich(0, dek.Fec + dek.Plc, "keine Verdeckung");
    }

    [Test]
    public static void Ende_BlendetAus_UndKlicktNachHundertMs()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 50, Netzsimulation.Konstant(30_000)), 3 * Sekunde, ausgabeMitschreiben: true);

        var begonnen = e.Ereignisse.Single(x => (x.Ereignis & QuellenEreignis.Begonnen) != 0);
        Pruefe.Wahr((begonnen.Ereignis & QuellenEreignis.KlickDruck) != 0, "Beginn-Klick bei BEGINN");
        var beendet = e.Ereignisse.Single(x => (x.Ereignis & QuellenEreignis.Beendet) != 0);
        var loslassen = e.Ereignisse.Single(x => (x.Ereignis & QuellenEreignis.KlickLoslassen) != 0);
        PruefeWert.Nahe(100_000, loslassen.ZeitUs - beendet.ZeitUs, 10_000, "Ende-Klick 100 ms nach dem Ende");

        // Letztes hörbares Sample vor der Stille ist ausgeblendet.
        var letztes = e.Ausgabe.FindLastIndex(v => v != 0f);
        Pruefe.Wahr(letztes > 0, "es gab Ausgabe");
        PruefeWert.Hoechstens(0.005, MathF.Abs(e.Ausgabe[letztes]), "letztes Sample");
        var davor = e.Ausgabe.Skip(letztes - 240).Take(240).Max(MathF.Abs);
        PruefeWert.Hoechstens(AttrappenDekodierer.Amplitude, davor, "Ausblendung über 5 ms");
    }

    [Test]
    public static void Ende_NeuePhaseBinnenHundertMs_OhneKlicks()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = Netzsimulation.Phase(1, 0, 50, Netzsimulation.Konstant(30_000));
        // Zielwechsel (C4): neue Phase direkt im Anschluss, Frame-Nr läuft weiter.
        pakete.AddRange(Netzsimulation.Phase(2, 53, 50, Netzsimulation.Konstant(30_000)));
        var e = Netzsimulation.Abspielen(jp, uhr, pakete, 4 * Sekunde);

        Pruefe.Gleich(1, e.Ereignisse.Count(x => (x.Ereignis & QuellenEreignis.KlickDruck) != 0), "nur der erste Beginn-Klick");
        Pruefe.Gleich(1, e.Ereignisse.Count(x => (x.Ereignis & QuellenEreignis.KlickLoslassen) != 0), "nur der letzte Ende-Klick");
        Pruefe.Gleich(2, e.Ereignisse.Count(x => (x.Ereignis & QuellenEreignis.Beendet) != 0), "zwei Phasen beendet");
        Pruefe.Gleich(100, dek.Normal, "alle Frames gespielt");
    }

    [Test]
    public static void OhneEnde_PhaseEndetDreihundertMsNachLeerlauf()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var e = Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 50, Netzsimulation.Konstant(30_000), mitEnde: false), 3 * Sekunde, ausgabeMitschreiben: true);
        var s = jp.Statistik;

        var letzterFrame = e.Frames.Where(f => f.Frame == 49).Single().ZeitUs;
        var beendet = e.Ereignisse.Single(x => (x.Ereignis & QuellenEreignis.Beendet) != 0);
        Pruefe.Wahr((beendet.Ereignis & QuellenEreignis.KlickLoslassen) != 0, "Ende-Klick sofort beim Zeitablauf");
        // Leer ab dem Rahmen nach Frame 49 (20 ms später), dann 300 ms.
        PruefeWert.Nahe(20_000 + 300_000, beendet.ZeitUs - letzterFrame, 20_000, "Phasenende nach Leerlauf");
        Pruefe.Gleich(1L, s.Unterlaeufe, "ein Unterlauf am Ende");
        PruefeWert.Hoechstens(60 * 48, dek.PlcSamples, "höchstens 60 ms verdeckt");
    }

    [Test]
    public static void Unterlauf_VerdecktOhneVorzuruecken_SpaetesPaketWirdGespielt()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        // Ab Frame 100 hängt das Netz 150 ms, danach kommt alles nach (Ziel 80 ms reicht nicht).
        Func<uint, long?> netz = f => f is >= 100 and < 108 ? 30_000 + (108 - f) * 20_000 : 30_000;
        Netzsimulation.Abspielen(jp, uhr, Netzsimulation.Phase(1, 0, 250, netz), 7 * Sekunde);
        var s = jp.Statistik;

        Pruefe.Gleich(1L, s.Unterlaeufe, "ein Unterlauf");
        Pruefe.Gleich(0L, s.SpaetVerworfen, "nichts verworfen: der Abspielpunkt wartet");
        Pruefe.Gleich(250, dek.Normal, "alle Frames gespielt");
        // Die Spitze geht in die Statistik, das Ziel steigt noch in der Phase (Anstieg um mehr als 20 ms).
        PruefeWert.Mindestens(150, s.ZielMs, "Ziel nach der Spitze");
    }

    [Test]
    public static void AltePhase_NachEnde_WirdVerworfen()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = Netzsimulation.Phase(5, 0, 20, Netzsimulation.Konstant(30_000));
        // Nachzügler der Phase 5 lange nach ihrem ENDE und ein Paket einer älteren Phase.
        pakete.Add(new SimPaket(2 * Sekunde, 5, 10, Sprachkennzeichen.Keine, AttrappenDekodierer.Paket(10)));
        pakete.Add(new SimPaket(2 * Sekunde + 1, 4, 11, Sprachkennzeichen.Keine, AttrappenDekodierer.Paket(11)));
        Netzsimulation.Abspielen(jp, uhr, pakete, 3 * Sekunde);
        var s = jp.Statistik;

        Pruefe.Gleich(1L, s.Phasen, "keine neue Phase");
        Pruefe.Gleich(2L, s.AltVerworfen, "beide verworfen");
        Pruefe.Gleich(20, dek.Normal, "nur die 20 Frames der Phase");
    }

    [Test]
    public static void FrameSprung_UeberDreitausend_NeueZeitlinie()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = Netzsimulation.Phase(1, 0, 50, Netzsimulation.Konstant(30_000), mitEnde: false);
        // Gleiche Phase, Frame-Nr springt um 5000 bei durchgehender Ankunft.
        pakete.AddRange(Netzsimulation.Phase(1, 5050, 50, f => 30_000 - 5000L * 20_000, mitEnde: true, mitBeginn: false));
        Netzsimulation.Abspielen(jp, uhr, pakete, 4 * Sekunde);

        Pruefe.Gleich(2L, jp.Statistik.Phasen, "Sprung beginnt eine neue Zeitlinie");
        Pruefe.Gleich(100, dek.Normal, "beide Hälften gespielt");
    }

    [Test]
    public static void MaxZiel_DeckeltZielverzoegerung()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen) { MaxZielMs = 60 };
        Netzsimulation.Abspielen(jp, uhr, VierPhasen(Netzsimulation.Buendel(30_000, 100_000)), 25 * Sekunde);
        PruefeWert.Hoechstens(60, jp.Statistik.ZielMs, "gedeckeltes Ziel");
    }

    [Test]
    public static void Echtzeit_EinreihenUndLesen_AllokierenNicht()
    {
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var pakete = VierPhasen(Netzsimulation.Verlust(Netzsimulation.Jitter(30_000, 60_000, 3), f => f % 17 == 0))
            .OrderBy(p => p.AnkunftUs).ToArray();
        var block = new float[480];
        var index = 0;
        var t = 3_000L;
        void Schritt()
        {
            while (index < pakete.Length && pakete[index].AnkunftUs <= t)
            {
                var p = pakete[index++];
                jp.Einreihen(p.AnkunftUs, p.Phase, p.Frame, p.Kennzeichen, p.Daten);
            }
            uhr.Us = t;
            jp.Lesen(block);
            t += 10_000;
        }
        for (var i = 0; i < 300; i++) Schritt(); // Aufwärmen
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 2000; i++) Schritt();
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in Einreihen/Lesen");
    }
}
