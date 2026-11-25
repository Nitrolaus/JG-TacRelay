// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangstufe C2a in der ganzen Kette (Prüfstand: Klangmaschine mit Geräte-Attrappe, simulierter Uhr, AEC3, RNNoise, Pegelstufe, Gate, Opus):
/// Punkt 2 (Handlungen auf der eigenen Seite: Echo geht nicht mehr hinaus, Kopplung im Echoschutz korrigiert, Wiedergabe leiser, Lernen aus,
/// Raumgeräusch hebt die Linie über ZusatzWunschDb, klemmende Taste), Punkt 3 über das Netz (Aktionen am "!" und die Taste aus Q8), Punkt 4
/// (Mikro stumm) und Punkt 5 im Klangkern (Schalter und Stufe).
/// </summary>
internal static class KlangC2MaschineTests
{
    private const int R = 480;

    /// <summary>Echo der Wiedergabe (Mittel beider Kanäle) um <paramref name="kopplung"/> gedämpft, <paramref name="verzugMs"/> später im Mikrofon.</summary>
    internal static void EchoAnlegen(Pruefstand s, Func<float, float> pfad, int verzugMs)
    {
        var linie = new Queue<float>(Enumerable.Repeat(0f, verzugMs * 48));
        s.Echo = (w, mic) =>
        {
            for (var i = 0; i < mic.Length; i++)
            {
                linie.Enqueue(pfad(0.5f * (w[2 * i] + w[2 * i + 1])));
                mic[i] += linie.Dequeue();
            }
        };
    }

    /// <summary>Phasen der Sprachaktivierung in den Paketen (BEGINN mit VA-Kennzeichen) ab Takt <paramref name="ab"/>.</summary>
    private static int VaPhasen(Pruefstand s, long ab = 0) =>
        s.Pakete.Count(p => p.Takt >= ab && (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0 && (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0);

    /// <summary>Ein Prüfstand mit gelernter Stimme (Pegelstufe fest +3 dB als gelernt), Sprachaktivierung, Echounterdrückung nach Wahl.</summary>
    private static async Task<Pruefstand> Aufbauen(bool echounterdrueckung)
    {
        var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = echounterdrueckung;
        await s.StartenAsync();
        return s;
    }

    [Test]
    public static async Task C2_Maschine_EchoOhneEchounterdrueckung_WirdErkannt_DanachGehtEsNichtMehrHinaus()
    {
        // Headset gewählt (Echounterdrückung aus), aber laut über offene Kopfhörer oder Lautsprecher: Moritz kommt 80 ms später mit -12 dB ins
        // Mikrofon, der Funker selbst schweigt.
        await using var s = await Aufbauen(echounterdrueckung: false);
        var moritz = KlangC2EmpfangTests.Korpusrede(150, out var phasen, saat: 41, pausenMs: 2000);
        var platz = s.Maschine.Mischer.QuelleHinzufuegen(new Funkquelle(moritz, phasen), 0);
        EchoAnlegen(s, x => 0.25f * x, verzugMs: 80);
        // Mikrofon: Raumrauschen; ab 80 s spricht der Funker selbst ab und zu (normal laut), auch während Moritz redet.
        var mikrofon = AnbindungSignale.Rauschen(150 * 48000, Pumpmessung.GrundrauschenDbfs, 7);
        var eigen = KlangC2EmpfangTests.Korpusrede(70, out var eigenePhasen, saat: 42, pausenMs: 3500, nurFunklaut: true);
        for (var i = 0; i < eigen.Length; i++) mikrofon[(80 * 48000) + i] += eigen[i];
        long erkannt = -1;
        var grundpegel = float.NaN;
        var urteilNachFuenf = DauersendUrteil.Keins;
        var stimmeNachFuenf = 0f;
        s.Abspielen(mikrofon, t =>
        {
            if (erkannt < 0 && s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Echo)
            {
                erkannt = t;
                grundpegel = s.Maschine.Kette.Pegelstufe!.GrundpegelDbfs;
            }
            if (erkannt > 0 && t == erkannt + 500)
            {
                urteilNachFuenf = s.Maschine.EigenesUrteil.Urteil;
                stimmeNachFuenf = s.Maschine.Mischer.Zustand(platz).EchoDaempfungDb;
            }
        });
        var vorher = VaPhasen(s) - VaPhasen(s, erkannt);
        var echoDanach = VaPhasen(s, erkannt + 300) - VaPhasen(s, 8000);
        var (eigeneHinaus, inPausen, inPausenHinaus) = EigeneSprueche(s, eigenePhasen, phasen);
        Console.WriteLine($"  C2 Maschine Echo ohne AEC: erkannt nach {erkannt / 100.0:0.0} s, {vorher} Phasen bis dahin, danach (ab +3 s bis 80 s) {echoDanach} Phasen; Kopplung {s.Maschine.Kette.Echoschutz.KopplungDb:0.0} dB, Stimme nach 5 s {stimmeNachFuenf:0.0} dB; eigene Sprüche ab 80 s {eigeneHinaus} von {eigenePhasen.Count}, in Moritz' Pausen {inPausenHinaus} von {inPausen}");
        Pruefe.Wahr(erkannt > 0, "Echo erkannt");
        Pruefe.Wahr(vorher > 0, "vorher ging Echo hinaus (die Szene misst etwas)");
        Pruefe.Gleich(DauersendUrteil.Echo, urteilNachFuenf, "Urteil hält, solange keine 30 s unter 0 vergangen sind");
        Pruefe.Wahr(DauersendUrteil.Echo.ToString().Length > 0 && Daempfungsworte.Ursache(DauersendUrteil.Echo).Contains("Lautsprecher", StringComparison.Ordinal),
            "Ursache in Wörtern mit Hinweis");
        PruefeWert.Hoechstens(1, echoDanach, "Echorahmen gehen nicht mehr hinaus");
        Pruefe.Wahr(s.Maschine.Kette.FunkspurSchutz, "ohne Echoreferenz vergleicht der Echoschutz jetzt mit der Funk-Wiedergabe");
        PruefeWert.Mindestens(-30, s.Maschine.Kette.Echoschutz.KopplungDb, "Kopplung gemessen (gegen die höchste Wiedergabe der letzten 400 ms)");
        PruefeWert.Hoechstens(-12, s.Maschine.Kette.Echoschutz.KopplungDb, "nicht über der wahren Kopplung");
        PruefeWert.Hoechstens(-1.9, stimmeNachFuenf, "die durchkommende Stimme ist leiser");
        PruefeWert.Mindestens(-6.0001, stimmeNachFuenf, "höchstens 6 dB");
        // Während Moritz redet, verschluckt RNNoise (Rauschfilter Standard) im Gemisch mit seinem Echo eigene Wörter um 30 bis 50 dB, mit und ohne
        // Wächter; der Wächter sperrt keinen (nächster Test, ohne Rauschfilter alle). Befund im Modulbericht, Abhilfe ist die Echounterdrückung.
        Pruefe.Gleich(inPausen, inPausenHinaus, "eigene Sprüche in Moritz' Pausen gehen alle hinaus");
        Pruefe.Wahr(float.IsFinite(grundpegel), "Stimme bekannt");
    }

    [Test]
    public static async Task C2_Maschine_EchoOhneEchounterdrueckung_WaechterSperrtKeinenEigenenSpruch()
    {
        // Dieselbe Szene ohne Rauschfilter (und mit DeepFilterNet3 ebenso, gemessen): Nach dem Urteil Echo gehen alle eigenen Sprüche hinaus,
        // auch die, die mitten in Moritz' Reden beginnen. Mit RNNoise fehlen davon zwei (vorheriger Test); das liegt am Rauschfilter, nicht am Wächter.
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = false;
        s.Maschine.Rauschfilter = Rauschfilter.Aus;
        await s.StartenAsync();
        var moritz = KlangC2EmpfangTests.Korpusrede(150, out var phasen, saat: 41, pausenMs: 2000);
        s.Maschine.Mischer.QuelleHinzufuegen(new Funkquelle(moritz, phasen), 0);
        EchoAnlegen(s, x => 0.25f * x, verzugMs: 80);
        var mikrofon = AnbindungSignale.Rauschen(150 * 48000, Pumpmessung.GrundrauschenDbfs, 7);
        var eigen = KlangC2EmpfangTests.Korpusrede(70, out var eigenePhasen, saat: 42, pausenMs: 3500, nurFunklaut: true);
        for (var i = 0; i < eigen.Length; i++) mikrofon[(80 * 48000) + i] += eigen[i];
        long erkannt = -1;
        s.Abspielen(mikrofon, t =>
        {
            if (erkannt < 0 && s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Echo) erkannt = t;
        });
        var (eigeneHinaus, inPausen, inPausenHinaus) = EigeneSprueche(s, eigenePhasen, phasen);
        Console.WriteLine($"  C2 Maschine Echo ohne AEC, ohne Rauschfilter: erkannt nach {erkannt / 100.0:0.0} s; eigene Sprüche {eigeneHinaus} von {eigenePhasen.Count} (in Pausen {inPausenHinaus} von {inPausen})");
        Pruefe.Wahr(erkannt is > 0 and < 8000, "Echo vor den eigenen Sprüchen erkannt");
        Pruefe.Gleich(eigenePhasen.Count, eigeneHinaus, "alle eigenen Sprüche gehen hinaus, auch während Moritz redet");
    }

    /// <summary>Eigene Sprüche ab 80 s, deren Phase innerhalb von 600 ms nach ihrem Anfang beginnt: alle, davon die in Moritz' Pausen.</summary>
    private static (int Hinaus, int InPausen, int InPausenHinaus) EigeneSprueche(Pruefstand s, List<(int Von, int Bis)> eigene, List<(int Von, int Bis)> moritz)
    {
        bool Hinaus((int Von, int Bis) p) => s.Pakete.Any(k => (k.Kennzeichen & Sprachkennzeichen.Beginn) != 0
            && k.Takt >= 8000 + (p.Von / R) - 20 && k.Takt <= 8000 + (p.Von / R) + 60);
        bool InPause((int Von, int Bis) p) => !moritz.Any(m => m.Von / R <= 8000 + (p.Von / R) + 60 && m.Bis / R >= 8000 + (p.Von / R) - 20);
        var pausen = eigene.Where(InPause).ToList();
        return (eigene.Count(Hinaus), pausen.Count, pausen.Count(Hinaus));
    }

    [Test]
    public static async Task C2_Maschine_SchalterUndStufe_GehenAnKetteUndMischer()
    {
        await using var s = await Aufbauen(echounterdrueckung: true);
        Pruefe.Gleich(DaempfungEinstellung.AbWerk, s.Maschine.Daempfung, "ab Werk an, deutlich leiser");
        s.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.FastWeg);
        s.Stille(2);
        Pruefe.Gleich(new DaempfungEinstellung(false, Daempfungsstufe.FastWeg), s.Maschine.Mischer.Daempfung, "Mischer");
        Pruefe.Falsch(s.Maschine.Kette.Waechter.Handeln, "Wächter handelt nicht");
        s.Maschine.Daempfung = DaempfungEinstellung.AbWerk;
        s.Stille(2);
        Pruefe.Wahr(s.Maschine.Kette.Waechter.Handeln, "wieder an");
    }

    [Test]
    public static async Task C2_Maschine_KlemmendeTaste_NachSechzigSekundenSendenAus_BisNeudruck()
    {
        await using var s = await Aufbauen(echounterdrueckung: false);
        var urteile = new List<WaechterStand>();
        s.Maschine.EigenesUrteilGeaendert += u => urteile.Add(u);
        s.Maschine.Sprachaktivierung = false;
        s.Maschine.SprechtasteGedrueckt();
        var raum = AnbindungSignale.Rauschen(70 * 48000, -55f, 9);
        s.Abspielen(raum);
        var letzte = s.Pakete.Last(p => p.Opus.Length > 0).Takt;
        Console.WriteLine($"  C2 klemmende Taste: letztes Paket nach {letzte / 100.0:0.0} s, Urteil {s.Maschine.EigenesUrteil.Urteil}");
        PruefeWert.Mindestens(5990, letzte, "eine Minute lang wird gesendet");
        PruefeWert.Hoechstens(6100, letzte, "danach nicht mehr");
        Pruefe.Wahr(s.Pakete.Any(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0), "die Phase endet sauber mit ENDE");
        Pruefe.Gleich(DauersendUrteil.KlemmendeTaste, s.Maschine.EigenesUrteil.Urteil, "Urteil");
        Pruefe.Wahr(urteile.Any(u => u.Urteil == DauersendUrteil.KlemmendeTaste), "Ereignis für die Oberfläche");
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(5);
        Pruefe.Gleich(DauersendUrteil.Keins, s.Maschine.EigenesUrteil.Urteil, "losgelassen: vorbei");
        var vorher = s.Pakete.Count;
        s.Maschine.SprechtasteGedrueckt();
        s.Stille(20);
        s.Maschine.SprechtasteLosgelassen();
        PruefeWert.Mindestens(vorher + 5, s.Pakete.Count, "neu gedrückt: sendet wieder");
    }

    [Test]
    public static async Task C2_Maschine_MikroStumm_VerwirftAlles_AuchTaste_GeraetOffen_EchounterdrueckungLaeuft_PegelStumm_Hinweis()
    {
        await using var s = await Aufbauen(echounterdrueckung: true);
        var hinweise = 0;
        s.Maschine.SprechtasteBeiStumm += () => hinweise++;
        var rede = KlangC2EmpfangTests.Korpusrede(30, out _, saat: 3, pausenMs: 300);
        var moritz = KlangC2EmpfangTests.Korpusrede(30, out var mp, saat: 4, pausenMs: 800);
        s.Maschine.Mischer.QuelleHinzufuegen(new Funkquelle(moritz, mp), 0);
        // Vier Sekunden normal: es wird gesendet.
        s.Abspielen(rede.AsSpan(0, 4 * 48000));
        Pruefe.Wahr(VaPhasen(s) > 0, "vorher sendet die Sprachaktivierung");
        var offen = s.Maschine.Kette.Fuehrung.Offen;
        var paketeVorher = s.Pakete.Count;
        var echoRahmen = s.Maschine.Kette.EchoRahmen;
        var grund = s.Maschine.Kette.Pegelstufe!.GrundpegelDbfs;
        var verstaerkung = s.Maschine.Kette.Pegelstufe!.VerstaerkungDb;
        s.Maschine.MikroStumm = true;
        var ab = s.Takte;
        var abMs = (s.Uhr.Us / 1000) + 50;
        s.Abspielen(rede.AsSpan(4 * 48000, 10 * 48000));
        s.Maschine.SprechtasteGedrueckt();
        s.Abspielen(rede.AsSpan(14 * 48000, 3 * 48000));
        s.Maschine.SprechtasteLosgelassen();
        var waehrend = s.Pakete.Where(p => p.Takt > ab).ToList();
        Console.WriteLine($"  C2 Mikro stumm: Gate beim Stummschalten offen {offen}, Pakete danach {waehrend.Count} ({string.Join(",", waehrend.Select(p => p.Kennzeichen))}), Hinweise {hinweise}");
        PruefeWert.Hoechstens(offen ? 2 : 0, waehrend.Count, "nichts gesendet, auch nicht mit Sprechtaste (höchstens der Abschluss der offenen Phase)");
        if (offen) Pruefe.Wahr((waehrend[^1].Kennzeichen & Sprachkennzeichen.Ende) != 0, "der Abschluss endet mit ENDE");
        if (offen) Pruefe.Wahr(waehrend.Any(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0), "eine offene Phase endet mit ENDE");
        Pruefe.Gleich(1, hinweise, "Sprechtaste bei stumm: ein Hinweis für die Meldezeile");
        Pruefe.Wahr(s.Meldungen.Where(m => m.ZeitMs > abMs).All(m => m.Stumm && !m.Sendet), "Pegelmeldung stumm");
        Pruefe.Falsch(s.System.Aufnahme!.Freigegeben, "das Gerät bleibt offen");
        PruefeWert.Mindestens(echoRahmen + 1200, s.Maschine.Kette.EchoRahmen, "die Echounterdrückung rechnet weiter");
        Pruefe.Gleich(grund, s.Maschine.Kette.Pegelstufe!.GrundpegelDbfs, "kein Lernen");
        Pruefe.Gleich(verstaerkung, s.Maschine.Kette.Pegelstufe!.VerstaerkungDb, "keine Verstärkungsänderung");
        Pruefe.Gleich(DauersendUrteil.Keins, s.Maschine.EigenesUrteil.Urteil, "kein Wächter");
        Pruefe.Wahr(s.Maschine.MikroStumm, "Zustand");

        s.Maschine.MikroStumm = false;
        var wieder = s.Takte;
        s.Abspielen(rede.AsSpan(17 * 48000, 8 * 48000));
        Pruefe.Wahr(VaPhasen(s, wieder) > 0, "sofort wieder da");
        Pruefe.Wahr(s.Meldungen.Last().Stumm == false, "Pegelmeldung wieder normal");
    }

    [Test]
    public static async Task C2_Maschine_Raumgeraeusch_LeiseEigeneStimme_LinieHoch_DanachFunksprueche()
    {
        await using var s = await Aufbauen(echounterdrueckung: false);
        s.Maschine.Daempfung = DaempfungEinstellung.AbWerk with { LeisesRedenSperren = true };
        // Q38: Sätze 12 dB unter der Funkstimme (leise zum Sohn), danach normale Funksprüche. Gleichmäßige Sätze: Ein anschwellender endet auch
        // 12 dB leiser nur 4 dB unter der Stimme, das ist nach Q38 kein leises Reden mehr (erster Lauf: er nahm den Wunsch zu Recht zurück).
        var leise = KlangC2EmpfangTests.Korpusrede(40, out var lp, saat: 12, pausenMs: 1500, pegelDb: -12f, nurGleichmaessig: true);
        var normal = KlangC2EmpfangTests.Korpusrede(40, out var np, saat: 13, pausenMs: 1500, nurFunklaut: true);
        // Vorher ein paar Funksprüche: Erst wer die Funkstimme in dieser Sitzung gehört hat, kennt ihren Abstand zum Rauschen und hält leise
        // Sprache mit weniger Abstand für Raumgeräusch (ein nur leiser gestelltes Mikrofon behält den Abstand, nächster Test).
        var vorher = KlangC2EmpfangTests.Korpusrede(15, out _, saat: 11, pausenMs: 1500, nurFunklaut: true);
        s.Abspielen(vorher);
        Pruefe.Gleich(DauersendUrteil.Keins, s.Maschine.EigenesUrteil.Urteil, "Funksprüche: kein Urteil");
        var abLeise = s.Takte;
        var gesperrtAb = -1L;
        s.Abspielen(leise, t =>
        {
            if (gesperrtAb < 0 && s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Raumgeraeusch) gesperrtAb = t;
        });
        var leiseHinaus = VaPhasen(s, abLeise);
        var spaeteLeise = VaPhasen(s, gesperrtAb + 200);
        var linie = s.Maschine.Kette.Fuehrung.ZusatzWunschDb;
        var linieWirksam = s.Maschine.Kette.Fuehrung.LinieDb;
        var nachLeise = s.Takte;
        s.Abspielen(normal);
        var normalHinaus = VaPhasen(s, nachLeise);
        Console.WriteLine($"  C2 Raum (Kette): gesperrt nach {(gesperrtAb - abLeise) / 100.0:0.0} s, leise Phasen hinaus {leiseHinaus} (nach der Sperre {spaeteLeise}), Wunsch {linie:0.0} dBFS; normale Sprüche {np.Count}, Phasen {normalHinaus}");
        Pruefe.Wahr(gesperrtAb > 0, "leise eigene Stimme: Raumgeräusch");
        PruefeWert.Hoechstens(4, leiseHinaus, "nach wenigen Phasen geht nichts Leises mehr hinaus");
        PruefeWert.Hoechstens(0, spaeteLeise, "danach keine leise Phase mehr");
        Pruefe.Wahr(float.IsFinite(linie), "Linie über ZusatzWunschDb angehoben");
        PruefeWert.Hoechstens(s.Maschine.Korridor.ObergrenzeDb + 1e-3, linieWirksam, "die Linie nie über der Obergrenze");
        Pruefe.Wahr(float.IsNegativeInfinity(s.Maschine.Kette.Fuehrung.ZusatzWunschDb), "Funkniveau: Linie wieder zurück");
        PruefeWert.Mindestens(np.Count - 1, normalHinaus, "die normalen Funksprüche gehen hinaus");
    }

    [Test]
    public static async Task C2_Maschine_MikrofonLeiserGestellt_KeinRaumgeraeusch()
    {
        // Das Mikrofon wird in Windows 15 dB leiser gestellt: Stimme und Rauschen sinken gleich, der Abstand bleibt. Das ist die Funkstimme, kein
        // leises Reden; der Wächter sperrt sie nicht (vorher: Raumgeräusch ohne Ende, 3 bis 18 % der Rahmen hinaus).
        await using var s = await Aufbauen(echounterdrueckung: false);
        s.Maschine.Daempfung = DaempfungEinstellung.AbWerk with { LeisesRedenSperren = true };
        var vorher = KlangC2EmpfangTests.Korpusrede(15, out _, saat: 11, pausenMs: 1500, nurFunklaut: true);
        var rausch = AnbindungSignale.Rauschen(vorher.Length, Pumpmessung.GrundrauschenDbfs, 5);
        for (var i = 0; i < vorher.Length; i++) vorher[i] += rausch[i];
        s.Abspielen(vorher);
        var leiser = KlangC2EmpfangTests.Korpusrede(60, out var lp, saat: 14, pausenMs: 1500, pegelDb: -15f, nurFunklaut: true);
        var rauschLeiser = AnbindungSignale.Rauschen(leiser.Length, Pumpmessung.GrundrauschenDbfs - 15f, 6);
        for (var i = 0; i < leiser.Length; i++) leiser[i] += rauschLeiser[i];
        var ab = s.Takte;
        var raum = false;
        s.Abspielen(leiser, _ => raum |= s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.Raumgeraeusch);
        var phasen = VaPhasen(s, ab);
        Console.WriteLine($"  C2 Mikrofon 15 dB leiser: Raumgeräusch {raum}, Phasen {phasen} bei {lp.Count} Sprüchen, Abstand der Stimme {s.Maschine.Kette.Waechter.StimmeAbstandDb:0.0} dB, Boden {s.Maschine.Kette.Waechter.BodenDb:0.0} dBFS");
        Pruefe.Falsch(raum, "kein Raumgeräusch");
        // Rückt das Mikrofon weiter weg (der Abstand zum Rauschen sinkt mit), urteilt er doch; dann sagt die Ursache, was zu tun ist.
        Pruefe.Wahr(Daempfungsworte.Ursache(DauersendUrteil.Raumgeraeusch).Contains("neu einmessen", StringComparison.Ordinal), "Hinweis für ein leiser gewordenes Mikrofon");
        PruefeWert.Mindestens(lp.Count - 1, phasen, "die Sprüche gehen hinaus");
    }

    [Test]
    public static async Task C2_Maschine_AktionenAmAusrufezeichen_UndTasteZuletztGedaempfter()
    {
        await using var s = await Aufbauen(echounterdrueckung: true);
        var geaendert = 0;
        s.Maschine.DauersenderGeaendert += () => geaendert++;
        // Max (Konto 7) echot Moritz (Konto 5); Anna (Konto 9) redet ab und zu. Pakete über das Netz (Opus, Jitterpuffer).
        var (moritz, max, anna) = MaxEchotMoritz(180);
        Zuspielen(s, [(5u, moritz), (7u, max), (9u, anna)], 180 * 100);
        var liste = s.Maschine.Dauersender;
        var maxZ = liste.SingleOrDefault(f => f.KontoId == 7);
        Console.WriteLine($"  C2 Aktionen: {string.Join("; ", liste.Select(f => $"{f.KontoId} {f.Urteil} {f.Wort}"))}, Ereignisse {geaendert}");
        Pruefe.Gleich(DauersendUrteil.Echo, maxZ.Urteil, "Max echot: Urteil an seinem Namen");
        Pruefe.Gleich("deutlich leiser", maxZ.Wort, "Dämpfung in Wörtern");
        Pruefe.Falsch(liste.Any(f => f.KontoId == 5 && f.Urteil != DauersendUrteil.Keins), "Moritz unberührt");
        Pruefe.Wahr(geaendert > 0, "Ereignis für die Oberfläche");

        // Q8: erster Druck stumm, zweiter wieder normal.
        var erst = s.Maschine.ZuletztGedaempftenUmschalten();
        Pruefe.Gleich(7u, erst?.KontoId, "der zuletzt gedämpfte ist Max");
        Pruefe.Wahr(erst!.Value.Stumm, "erster Druck: stumm");
        s.Stille(5);
        Pruefe.Wahr(s.Maschine.Dauersender.Single(f => f.KontoId == 7).Stumm, "stumm in der Liste");
        Pruefe.Gleich("stumm", s.Maschine.Dauersender.Single(f => f.KontoId == 7).Wort, "Wort");
        var zweit = s.Maschine.ZuletztGedaempftenUmschalten();
        Pruefe.Falsch(zweit!.Value.Stumm, "zweiter Druck: wieder normal");
        s.Stille(5);
        var z = s.Maschine.Dauersender.SingleOrDefault(f => f.KontoId == 7);
        Pruefe.Falsch(z.Stumm, "nicht mehr stumm");
        PruefeWert.Mindestens(-0.01, z.DaempfungDb, "wieder normal heißt: ungedämpft");

        // Am "!": für diesen Funker nicht mehr dämpfen.
        s.Maschine.FunkerHandeln(7, FunkerAktion.NichtMehrDaempfen);
        Zuspielen(s, [(5u, moritz), (7u, max), (9u, anna)], 120 * 100, phasenAb: 1000);
        var n = s.Maschine.Dauersender.Single(f => f.KontoId == 7);
        Pruefe.Wahr(n.NichtDaempfen, "gemerkt");
        Pruefe.Gleich(0f, n.DaempfungDb, "nicht mehr gedämpft, auch wenn er weiter echot");
        s.Maschine.FunkerHandeln(7, FunkerAktion.Stumm);
        s.Stille(5);
        Pruefe.Wahr(s.Maschine.Dauersender.Single(f => f.KontoId == 7).Stumm, "am Ausrufezeichen stumm");
        s.Maschine.FunkerHandeln(7, FunkerAktion.WiederNormal);
        s.Stille(5);
        var w = s.Maschine.Dauersender.SingleOrDefault(f => f.KontoId == 7);
        Pruefe.Falsch(w.Stumm || w.NichtDaempfen, "wieder normal hebt beides auf");
    }

    /// <summary>Drei Ströme wie in <see cref="KlangC2EmpfangTests"/>: Moritz, Max echot Moritz, Anna redet ab und zu.</summary>
    internal static ((float[] S, List<(int, int)> P) Moritz, (float[] S, List<(int, int)> P) Max, (float[] S, List<(int, int)> P) Anna) MaxEchotMoritz(int sekunden)
    {
        var moritz = KlangC2EmpfangTests.Korpusrede(sekunden, out var mp, saat: 51, pausenMs: 2500);
        var echo = new float[moritz.Length];
        var ep = new List<(int, int)>();
        foreach (var (von, bis) in mp)
        {
            var a = von + 150 * 48;
            var b = Math.Min(echo.Length, bis + 500 * 48);
            for (var i = a; i < b; i++) echo[i] = (i - 200 * 48 >= 0 ? moritz[i - 200 * 48] * 0.5f : 0f) + 0.0005f * MathF.Sin(i * 0.3f);
            ep.Add((a, b));
        }
        var anna = KlangC2EmpfangTests.Korpusrede(sekunden, out var ap, saat: 52, pausenMs: 5000);
        return ((moritz, mp), (echo, ep), (anna, ap));
    }

    /// <summary>Kodiert die Ströme mit Opus (je Phase BEGINN bis ENDE) und spielt sie im 10-ms-Takt über das Netz in die Maschine.</summary>
    internal static void Zuspielen(Pruefstand s, IReadOnlyList<(uint Konto, (float[] S, List<(int Von, int Bis)> P) Strom)> stroeme, int takte, ushort phasenAb = 0)
    {
        var pakete = stroeme.Select(x => Kodieren(x.Strom.S, x.Strom.P, phasenAb)).ToList();
        var naechstes = new int[stroeme.Count];
        var stille = new float[R];
        for (var t = 0; t < takte; t++)
        {
            for (var k = 0; k < stroeme.Count; k++)
            {
                var liste = pakete[k];
                while (naechstes[k] < liste.Count && liste[naechstes[k]].Takt <= t)
                {
                    var p = liste[naechstes[k]++];
                    _ = s.Maschine.SpracheEmpfangen(stroeme[k].Konto, 0, p.Phase, p.Frame, p.Kennzeichen, p.Opus, s.Uhr.Us);
                }
            }
            s.Takt(stille);
        }
    }

    private static List<(long Takt, ushort Phase, uint Frame, Sprachkennzeichen Kennzeichen, byte[] Opus)> Kodieren(float[] signal, List<(int Von, int Bis)> phasen, ushort phasenAb)
    {
        using var k = new OpusKodierer(OpusAnwendung.Voip, OpusKodierer.StandardBitrate);
        var liste = new List<(long, ushort, uint, Sprachkennzeichen, byte[])>();
        var puffer = new byte[400];
        var phase = phasenAb;
        foreach (var (von, bis) in phasen)
        {
            phase++;
            uint frame = 0;
            for (var pos = von; pos + 960 <= bis; pos += 960)
            {
                var n = k.Kodieren(signal.AsSpan(pos, 960), puffer);
                liste.Add(((pos + 960) / R, phase, frame++, frame == 1 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer.AsSpan(0, n).ToArray()));
            }
            liste.Add(((bis / R) + 1, phase, frame, Sprachkennzeichen.Ende, []));
        }
        return liste;
    }
}
