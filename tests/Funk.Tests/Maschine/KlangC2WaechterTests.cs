// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangstufe C2a, Punkt 1: der Wächter der Auto Dauersend-Dämpfung (KLANGSTUFE-C-ENTWURF.md im Planungsordner, "Die Formel", "Was Dauersenden ist",
/// Q38). Die Formel mit den Zahlen der Tabelle im Entwurf, das Punktekonto (ab ln(1000) erkannt, 30 s unter 0 Entwarnung), die drei
/// Beobachtungen je Sprechphase und die Urteile an synthetischen Rahmenfolgen (Pegel je 10 ms, ein kleines Gate wie das echte: Linie,
/// Bestätigung 4 Rahmen, Nachlauf 300 ms). Die nachgestellten Gespräche mit der ganzen Kette stehen in <see cref="MessungKlangC2"/>.
/// </summary>
internal static class KlangC2WaechterTests
{
    // ------------------------------------------------------------------------------------------------------------------------------
    // Formel und Konto
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C2_Formel_PunkteWieInDerTabelleDesEntwurfs()
    {
        var k = WaechterKalibrierung.Entwurf;
        PruefeWert.Nahe(Math.Log(1000), Waechterformel.ErkanntPunkte, 1e-5, "erkannt ab ln(1000)");
        // 4 Sprecher: a = 0,3; 0,3 Anfänge je Sekunde.
        PruefeWert.Nahe(1.15, Waechterformel.Ueberlappung(true, 0.3f, k), 0.01, "4 Sprecher: Überlappung ja");
        PruefeWert.Nahe(-2.64, Waechterformel.Ueberlappung(false, 0.3f, k), 0.01, "4 Sprecher: Überlappung nein");
        PruefeWert.Nahe(2.15, Waechterformel.Anlauf(true, 0.3f, k), 0.01, "4 Sprecher: Anlauf ja (Zufall 0,3 mal 0,35 s)");
        PruefeWert.Nahe(-2.19, Waechterformel.Anlauf(false, 0.3f, k), 0.01, "4 Sprecher: Anlauf nein");
        // 25 Sprecher: a = 0,8; 1 Anfang je Sekunde.
        PruefeWert.Nahe(0.17, Waechterformel.Ueberlappung(true, 0.8f, k), 0.01, "25 Sprecher: Überlappung ja");
        PruefeWert.Nahe(-1.39, Waechterformel.Ueberlappung(false, 0.8f, k), 0.01, "25 Sprecher: Überlappung nein");
        PruefeWert.Nahe(0.94, Waechterformel.Anlauf(true, 1f, k), 0.01, "25 Sprecher: Anlauf ja");
        PruefeWert.Nahe(-1.87, Waechterformel.Anlauf(false, 1f, k), 0.01, "25 Sprecher: Anlauf nein");
        // Keine Beobachtung bringt mehr als den Höchstwert, auch nicht bei a nahe 0.
        PruefeWert.Hoechstens(Waechterformel.PunkteHoechst, Waechterformel.Ueberlappung(true, 0f, k), "begrenzt");
        PruefeWert.Mindestens(-Waechterformel.PunkteHoechst, Waechterformel.Anlauf(false, 0f, k with { AnlaufBeiEcho = 1f }), "begrenzt nach unten");
        // Gleichlauf: eingemessen (ab Werk) steigt er mit der Korrelation und trennt Echo von Doppelsprechen.
        var w = WaechterKalibrierung.AbWerk;
        PruefeWert.Mindestens(1, w.Gleichlauf(0.9f), "ab Werk: hoher Gleichlauf spricht für Echo");
        PruefeWert.Hoechstens(-1, w.Gleichlauf(0.0f), "ab Werk: kein Gleichlauf spricht dagegen");
        for (var r = -1f; r < 1f; r += 0.05f) PruefeWert.Mindestens(w.Gleichlauf(r) - 1e-6, w.Gleichlauf(MathF.Min(1f, r + 0.05f)) - 0, "monoton");
    }

    [Test]
    public static void C2_Konto_ErkenntAbLn1000_Entwarnung30sUnterNull_GrenzenUntenUndOben()
    {
        var k = new Punktekonto();
        k.Buchen(3.3f, 100);
        k.Buchen(3.3f, 200);
        Pruefe.Falsch(k.Erkannt, "6,6 Punkte: noch nicht");
        k.Buchen(3.3f, 300);
        Pruefe.Wahr(k.Erkannt, "9,9 Punkte: erkannt");
        PruefeWert.Hoechstens(Waechterformel.ObergrenzePunkte, k.Stand, "nach oben begrenzt");
        for (var i = 0; i < 10; i++) k.Buchen(3f, 400 + i);
        Pruefe.Gleich(Waechterformel.ObergrenzePunkte, k.Stand, "Obergrenze");
        k.Buchen(-30f, 500);
        Pruefe.Gleich(Waechterformel.UntergrenzePunkte, k.Stand, "Untergrenze");
        k.Takt(500 + Waechterformel.EntwarnungRahmen - 1);
        Pruefe.Wahr(k.Erkannt, "noch keine 30 s unter 0");
        k.Takt(500 + Waechterformel.EntwarnungRahmen);
        Pruefe.Falsch(k.Erkannt, "30 s unter 0: Entwarnung");

        // Zwischendurch über 0 beginnt die Frist neu.
        var j = new Punktekonto();
        for (var i = 0; i < 3; i++) j.Buchen(3f, i);
        j.Buchen(-10f, 1000);
        j.Takt(2000);
        j.Buchen(5f, 2500);
        j.Buchen(-10f, 2600);
        j.Takt(1000 + Waechterformel.EntwarnungRahmen + 100);
        Pruefe.Wahr(j.Erkannt, "die Frist lief ab 2600 neu");
        j.Takt(2600 + Waechterformel.EntwarnungRahmen);
        Pruefe.Falsch(j.Erkannt, "30 s nach dem letzten Fall unter 0");
    }

    [Test]
    public static void C2_Beobachter_GleichlaufFindetVerschiebung_UnabhaengigeStimmeNicht()
    {
        var r = new Random(5);
        var funk = Synth.Rede(3000, r, -28f);
        var spur = new Referenzspur();
        var echo = new Phasenbeobachter();
        var eigen = new Phasenbeobachter();
        var eigeneStimme = Synth.Rede(3000, new Random(9), -26f);
        echo.Beginnen(0, anlauf: true);
        eigen.Beginnen(0, anlauf: false);
        for (var f = 0; f < 3000; f++)
        {
            spur.Hinzufuegen(funk[f]);
            var e = f >= 12 ? Synth.Summe(funk[f - 12] - 10f, -65f) + (float)(r.NextDouble() - 0.5) : -65f;
            echo.Rahmen(e, e, float.NaN, 0.95f, funkLaeuft: true, spur, funkPlatz: 3, spielNichtSprache: false);
            eigen.Rahmen(eigeneStimme[f], eigeneStimme[f], float.NaN, 0.95f, funkLaeuft: true, spur, funkPlatz: 3, spielNichtSprache: false);
        }
        var b = echo.Ergebnis();
        Console.WriteLine($"  C2 Gleichlauf Echo {b.Gleichlauf:0.00} bei {b.BesteVerschiebung} Rahmen, eigene Stimme {eigen.Ergebnis().Gleichlauf:0.00}");
        PruefeWert.Mindestens(0.8, b.Gleichlauf, "Echo: hoher Gleichlauf");
        Pruefe.Gleich(12, b.BesteVerschiebung, "beste Verschiebung 120 ms");
        PruefeWert.Hoechstens(0.3, eigen.Ergebnis().Gleichlauf, "unabhängige Stimme: kaum Gleichlauf");
        Pruefe.Wahr(b.Ueberlappung, "Funk lief");
        Pruefe.Wahr(b.Anlauf, "Anlauf gemerkt");
        Pruefe.Gleich(3, b.FunkPlatz, "Platz des durchkommenden Funkers");
        PruefeWert.Mindestens(0.9, b.SprachAnteil, "Sprachanteil");

        // Kurze Phase: kein Gleichlauf (zu wenig Rahmen), aber Überlappung.
        var kurz = new Phasenbeobachter();
        kurz.Beginnen(0, anlauf: false);
        for (var f = 0; f < 10; f++) kurz.Rahmen(-30f, -30f, float.NaN, 0.9f, funkLaeuft: false, spur, -1, false);
        Pruefe.Wahr(float.IsNaN(kurz.Ergebnis().Gleichlauf), "unter 200 ms kein Gleichlauf");
        Pruefe.Falsch(kurz.Ergebnis().Ueberlappung, "ohne Funk keine Überlappung");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Urteile an synthetischen Rahmenfolgen
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C2_Waechter_Echo_4Und25Sprecher_ErkanntInWenigenPhasen_EigeneStimmeNie()
    {
        foreach (var (sprecher, hoechstens) in new[] { (4, 3), (25, 4) })
        {
            // Erst 3 Minuten Gespräch ohne Echo (eigene Stimme spricht oft, auch über die anderen), dann Echo ohne eigene Stimme.
            var s = Synth.Gespraech(sprecher, sekunden: 180, eigeneAnteil: 0.25, saat: 40 + sprecher);
            var lauf = new Synth.Lauf(new EigenerWaechter(WaechterKalibrierung.AbWerk));
            lauf.Abspielen(s);
            Pruefe.Gleich(DauersendUrteil.Keins, lauf.HoechstesUrteil, $"{sprecher} Sprecher: im Gespräch kein Urteil");
            Pruefe.Falsch(lauf.W.EchoErkannt, "kein Echo");
            var echo = Synth.Gespraech(sprecher, sekunden: 120, eigeneAnteil: 0, saat: 80 + sprecher, echoKopplungDb: -12f);
            var phasenVorher = lauf.Phasen;
            var bis = lauf.AbspielenBis(echo, () => lauf.W.EchoErkannt);
            var phasen = lauf.Phasen - phasenVorher;
            Console.WriteLine($"  C2 Echo {sprecher} Sprecher: erkannt nach {phasen} Phasen, {bis / 100.0:0.0} s, Konto {lauf.W.EchoKonto:0.0}");
            Pruefe.Wahr(lauf.W.EchoErkannt, $"{sprecher} Sprecher: Echo erkannt");
            PruefeWert.Hoechstens(hoechstens, phasen, $"{sprecher} Sprecher: nach wenigen Phasen");
            Pruefe.Gleich(DauersendUrteil.Echo, lauf.W.Urteil, "Urteil Echo");
            Pruefe.Falsch(lauf.W.LernenErlaubt, "während des Urteils lernt die Pegelstufe nicht");
            // Gemessen wie im Echoschutz: Mikrofon gegen den höchsten Wiedergabepegel der letzten 400 ms (hier der Funk), also etwas unter
            // der Kopplung der Mittelwerte (-12 dB); mit den 10 dB Sicherheit des Echoschutzes deckt sie das Echo ab.
            PruefeWert.Mindestens(-20, lauf.W.KopplungDb, "gemessene Kopplung nicht zu tief");
            PruefeWert.Hoechstens(-10, lauf.W.KopplungDb, "gemessene Kopplung nicht über dem Echo");
        }
    }

    [Test]
    public static void C2_Waechter_Raumgeraeusch_LeiseStimmeZwoelfDbUnterDerFunkstimme_SperrtNachWenigenPhasen_FunkniveauNimmtZuegigZurueck()
    {
        foreach (var sprecher in new[] { 4, 25 })
        {
            var w = new EigenerWaechter(WaechterKalibrierung.AbWerk) { RaumAn = true };
            var lauf = new Synth.Lauf(w) { StimmeDb = -26f };
            lauf.Abspielen(Synth.Gespraech(sprecher, sekunden: 60, eigeneAnteil: 0.2, saat: 3));
            Pruefe.Falsch(w.RaumErkannt, "normales Gespräch: kein Raumgeräusch");
            // Leise zum Sohn: Sätze 12 dB unter der Funkstimme, hohe Sprachwahrscheinlichkeit, kein Echo.
            var leise = Synth.Gespraech(sprecher, sekunden: 40, eigeneAnteil: 0.4, saat: 7, eigenPegelDb: -38f);
            var vorher = lauf.Phasen;
            lauf.AbspielenBis(leise, () => w.RaumErkannt);
            var phasen = lauf.Phasen - vorher;
            Console.WriteLine($"  C2 Raum {sprecher} Sprecher: erkannt nach {phasen} leisen Phasen, Wunsch {w.RaumWunschVorStufeDb:0.0} dBFS vor der Pegelstufe");
            Pruefe.Wahr(w.RaumErkannt, "leise Stimme gilt als nicht für den Funk bestimmt");
            PruefeWert.Hoechstens(3, phasen, "nach wenigen Phasen");
            Pruefe.Gleich(DauersendUrteil.Raumgeraeusch, w.Urteil, "Urteil");
            PruefeWert.Mindestens(-38, w.RaumWunschVorStufeDb, "Linie über der leisen Stimme");
            PruefeWert.Hoechstens(-26 - 2, w.RaumWunschVorStufeDb, "höchstens Stimme minus 2 dB (die Linie selbst deckelt die GateFuehrung bei minus 6)");
            Pruefe.Falsch(w.LernenErlaubt, "lernt nicht");
            // Die nächste Äußerung auf Funkniveau (nach einer Pause von 300 ms) nimmt den Wunsch nach 200 ms Sprache zurück, noch im Anlauf.
            var normal = new WaechterRahmen { EigenDb = -24f, EingangDb = -24f, P = 0.97f, Gate = GateZustand.Anlauf, FunkDb = -90f, StimmeDb = -26f, WiedergabeDb = float.NaN, SpielDb = float.NaN };
            for (var i = 0; i < EigenerWaechter.AeusserungPauseRahmen; i++) w.Rahmen(normal with { Gate = GateZustand.Leise, P = 0.02f, EigenDb = -65f, EingangDb = -65f });
            for (var i = 0; i < EigenerWaechter.AeusserungFunkniveauRahmen - 1; i++) w.Rahmen(normal);
            Pruefe.Falsch(float.IsNegativeInfinity(w.RaumWunschVorStufeDb), "vor 200 ms Sprache noch nicht");
            w.Rahmen(normal);
            Pruefe.Wahr(float.IsNegativeInfinity(w.RaumWunschVorStufeDb), "Funkniveau (200 ms Sprache der Äußerung): Wunsch zurück");
            Pruefe.Falsch(w.RaumErkannt, "Urteil vorbei");
        }
    }

    [Test]
    public static void C2_Waechter_Spielton_NichtSprachartigeWiedergabeKaumSprache_MehrAbstandBisRuhe()
    {
        var w = new EigenerWaechter(WaechterKalibrierung.AbWerk);
        var lauf = new Synth.Lauf(w) { StimmeDb = -26f };
        var spiel = Synth.Spielphasen(sekunden: 30, saat: 4);
        lauf.AbspielenBis(spiel, () => w.SpieltonErkannt);
        Pruefe.Wahr(w.SpieltonErkannt, "Spielton erkannt");
        Pruefe.Gleich(DauersendUrteil.Spielton, w.Urteil, "Urteil Spielton");
        PruefeWert.Mindestens(2, w.GeraeuschZusatzDb, "der Echoschutz verlangt mehr Abstand");
        Pruefe.Falsch(w.RaumErkannt, "kein Raumgeräusch daneben");
        lauf.Abspielen(Synth.Stille(sekunden: 31));
        Pruefe.Falsch(w.SpieltonErkannt, "30 s ohne Verdacht: vorbei");
        Pruefe.Gleich(0f, w.GeraeuschZusatzDb, "Abstand wieder wie ab Werk");
    }

    [Test]
    public static void C2_Waechter_KlemmendeTaste_SechzigSekundenFastOhneSprache_ErstNeudruckLoest()
    {
        var w = new EigenerWaechter(WaechterKalibrierung.AbWerk);
        var r = new WaechterRahmen { EigenDb = -60f, EingangDb = -60f, P = 0.05f, Taste = true, Gate = GateZustand.Senden, FunkDb = -90f, StimmeDb = -26f, WiedergabeDb = float.NaN, SpielDb = float.NaN };
        for (var i = 0; i < 5999; i++) w.Rahmen(r);
        Pruefe.Falsch(w.TasteKlemmt, "vor 60 s nicht");
        w.Rahmen(r);
        Pruefe.Wahr(w.TasteKlemmt, "nach 60 s fast ohne Sprache: Senden aus");
        Pruefe.Gleich(DauersendUrteil.KlemmendeTaste, w.Urteil, "Urteil");
        for (var i = 0; i < 100; i++) w.Rahmen(r with { P = 0.99f });
        Pruefe.Wahr(w.TasteKlemmt, "bleibt aus, solange die Taste gehalten wird, auch wenn jetzt gesprochen wird");
        w.Rahmen(r with { Taste = false, Gate = GateZustand.Leise });
        Pruefe.Falsch(w.TasteKlemmt, "losgelassen: der nächste Druck sendet wieder");
        Pruefe.Gleich(DauersendUrteil.Keins, w.Urteil, "kein Urteil mehr");

        // Eine lange Ansage mit der Taste (fünf Minuten, Sprache in gut der Hälfte der Rahmen) klemmt nie.
        var ansage = new Random(2);
        for (var i = 0; i < 30000; i++) w.Rahmen(r with { P = ansage.NextDouble() < 0.55 ? 0.95f : 0.1f, EigenDb = -26f });
        Pruefe.Falsch(w.TasteKlemmt, "lange Ansage mit Taste");
    }

    [Test]
    public static void C2_Wiedergabe_DurchkommendeStimmeSchrittweise_HoechstensSechs_DannGesamt_FreigabeLangsam_GrenzeJeGeraet()
    {
        var wg = new WiedergabeAttrappe();
        var w = new EigenerWaechter(WaechterKalibrierung.AbWerk, wg);
        var lauf = new Synth.Lauf(w);
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 60, eigeneAnteil: 0, saat: 11));
        lauf.AbspielenBis(Synth.Gespraech(4, sekunden: 300, eigeneAnteil: 0, saat: 12, echoKopplungDb: -12f), () => wg.GesamtDb <= -6f);
        Pruefe.Wahr(w.EchoErkannt, "Echo erkannt");
        Pruefe.Wahr(wg.Schritte.Count > 0, "Schritte gemacht");
        var stimmen = wg.Schritte.Where(s => s.Platz >= 0).ToList();
        Pruefe.Wahr(stimmen.Count > 0, "zuerst die durchkommende Stimme");
        Pruefe.Wahr(stimmen.All(s => s.Db >= -6f - 1e-4), "die Stimme höchstens 6 dB leiser");
        Pruefe.Wahr(wg.Schritte.TakeWhile(s => s.Platz >= 0).Any(), "Stimme vor der ganzen Wiedergabe");
        PruefeWert.Mindestens(-6f - 1e-4, wg.GesamtDb, "die ganze Funk-Wiedergabe höchstens 6 dB leiser");
        PruefeWert.Hoechstens(2f + 1e-4, wg.Schritte.Zip(wg.Schritte.Skip(1)).Where(p => p.First.Platz == p.Second.Platz).Select(p => p.First.Db - p.Second.Db).DefaultIfEmpty(0).Max(), "schrittweise (höchstens 2 dB je Phase)");

        // Echo hört auf (Lautsprecher leiser): unbeantwortete Funk-Anfänge bringen das Konto unter 0, nach 30 s darunter Entwarnung, dann
        // langsam zurück (höchstens 1 dB je 10 s).
        var bisRuhe = lauf.AbspielenBis(Synth.Gespraech(4, sekunden: 180, eigeneAnteil: 0, saat: 13), () => !w.EchoErkannt);
        Console.WriteLine($"  C2 Entwarnung nach {bisRuhe / 100.0:0.0} s ohne Echo");
        Pruefe.Falsch(w.EchoErkannt, "Entwarnung");
        PruefeWert.Mindestens(3000, bisRuhe, "nicht vor 30 s");
        Pruefe.Gleich(-6f, wg.GesamtDb, "bis zur Entwarnung bleibt es leiser");
        var vorher = wg.GesamtDb;
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 20, eigeneAnteil: 0, saat: 14));
        PruefeWert.Hoechstens(vorher + 2f + 1e-4, wg.GesamtDb, "Freigabe langsamer als Eingriff");
        PruefeWert.Mindestens(vorher + 1f - 1e-4, wg.GesamtDb, "aber sie kommt");
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 120, eigeneAnteil: 0, saat: 15));
        Pruefe.Gleich(0f, wg.GesamtDb, "erstes Mal: ganz zurück");

        // Kommt das Echo auf diesem Gerät wieder, merkt sich der Wächter die Grenze und gibt nur noch bis knapp darunter frei.
        lauf.AbspielenBis(Synth.Gespraech(4, sekunden: 300, eigeneAnteil: 0, saat: 16, echoKopplungDb: -12f), () => w.EchoErkannt);
        PruefeWert.Hoechstens(-2f, w.GrenzeDb, "Grenzlautstärke gemerkt");
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 600, eigeneAnteil: 0, saat: 17));
        PruefeWert.Hoechstens(w.GrenzeDb + 1e-4, wg.GesamtDb, "Freigabe nur bis zur Grenze");
    }

    [Test]
    public static void C2_Waechter_SchalterAus_UrteiltAberHandeltNicht()
    {
        var wg = new WiedergabeAttrappe();
        var w = new EigenerWaechter(WaechterKalibrierung.AbWerk, wg) { Handeln = false };
        var lauf = new Synth.Lauf(w);
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 60, eigeneAnteil: 0, saat: 21));
        lauf.Abspielen(Synth.Gespraech(4, sekunden: 120, eigeneAnteil: 0, saat: 22, echoKopplungDb: -12f));
        Pruefe.Wahr(w.EchoErkannt, "das Urteil kommt trotzdem (für das gelbe Zeichen)");
        Pruefe.Gleich(0, wg.Schritte.Count, "aber keine Wiedergabe wird leiser");
        Pruefe.Wahr(float.IsNaN(w.KopplungDb) || w.KopplungFassung == 0, "keine Korrektur am Echoschutz");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 2: Handlungen an den vorhandenen Reglern
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C2_Echoschutz_KopplungNurNachObenKorrigiert_MehrAbstandBeiSpielton()
    {
        var e = new Echoschutz();
        // Gelernt hat er eine zu tiefe Kopplung (-30 dB), etwa weil das Echo kam, während das Gate offen war.
        for (var i = 0; i < 1000; i++)
        {
            e.Wiedergabe(-30f);
            _ = e.Verdacht(-60f, gateOffen: false, sprechtaste: false);
        }
        PruefeWert.Nahe(-30, e.KopplungDb, 0.2, "gelernt");
        Pruefe.Falsch(e.Verdacht(-42f, gateOffen: false, sprechtaste: false), "das Echo (-42 dBFS) gilt nicht als Echo");
        e.KopplungKorrigieren(-12f);
        Pruefe.Gleich(-12f, e.KopplungDb, "korrigiert auf das Gemessene");
        e.KopplungKorrigieren(-20f);
        Pruefe.Gleich(-12f, e.KopplungDb, "nur nach oben");
        e.KopplungKorrigieren(float.NaN);
        Pruefe.Gleich(-12f, e.KopplungDb, "NaN ändert nichts");
        e.Wiedergabe(-30f);
        Pruefe.Wahr(e.Verdacht(-42f, gateOffen: false, sprechtaste: false), "jetzt beginnt das Echo keine Phase");
        Pruefe.Falsch(e.Verdacht(-20f, gateOffen: false, sprechtaste: false), "die eigene Stimme 10 dB über dem Echo schon");

        // Spielton: Abstand 6 dB ab Werk, der Wächter verlangt mehr.
        var s = new Echoschutz();
        s.KopplungKorrigieren(-12f);
        s.Wiedergabe(-30f, sprachartig: false);
        Pruefe.Falsch(s.Verdacht(-35f, gateOffen: true, sprechtaste: false), "6 dB über dem Spielecho: darf öffnen");
        s.WaechterAbstandDb = 4f;
        Pruefe.Wahr(s.Verdacht(-35f, gateOffen: true, sprechtaste: false), "mit 4 dB mehr Abstand nicht");
        s.WaechterAbstandDb = -3f;
        Pruefe.Gleich(0f, s.WaechterAbstandDb, "nie weniger Abstand als ab Werk");
    }

    [Test]
    public static void C2_GateFuehrung_ZusatzWunsch_AuchImModusFest_NieUeberDieObergrenze()
    {
        float Linie(GateFuehrung f)
        {
            _ = f.Vorbereiten(-80f, 0f, false);
            return f.LinieDb;
        }
        var fest = new GateFuehrung { Einstellung = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -45f }, StimmeDb = -23f };
        Pruefe.Gleich(-45f, Linie(fest), "Fest ohne Wunsch: die gezogene Schwelle");
        fest.ZusatzWunschDb = -35f;
        Pruefe.Gleich(-35f, Linie(fest), "Q9: bei Raumgeräusch unempfindlicher, auch in Fest");
        fest.ZusatzWunschDb = -10f;
        Pruefe.Gleich(-29f, Linie(fest), "nie über die Obergrenze (Stimme minus 6 dB)");
        fest.ZusatzWunschDb = -60f;
        Pruefe.Gleich(-45f, Linie(fest), "ein Wunsch unter der Schwelle ändert nichts (Maximum)");
        fest.ZusatzWunschDb = float.NegativeInfinity;
        fest.StimmeDb = float.NaN;
        fest.ZusatzWunschDb = -35f;
        Pruefe.Gleich(-45f, Linie(fest), "ohne gelernte Stimme keine Obergrenze, also kein Wunsch");
        var auto = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = float.NaN, ZusatzWunschDb = -20f };
        PruefeWert.Hoechstens(-40, Linie(auto), "auch in Automatisch nicht ohne Stimme");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Hilfen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Merkt jeden Schritt an der Wiedergabe (Platz -1: die ganze Funk-Wiedergabe).</summary>
    internal sealed class WiedergabeAttrappe : IEchoWiedergabe
    {
        private readonly float[] _stimme = new float[64];

        public List<(int Platz, float Db)> Schritte { get; } = [];

        public float GesamtDb { get; set; }

        float IEchoWiedergabe.GesamtDb
        {
            get => GesamtDb;
            set
            {
                if (value != GesamtDb) Schritte.Add((-1, value));
                GesamtDb = value;
            }
        }

        public float StimmeDb(int platz) => platz is >= 0 and < 64 ? _stimme[platz] : 0f;

        public void StimmeSetzen(int platz, float db)
        {
            if (platz is < 0 or >= 64) return;
            if (db != _stimme[platz]) Schritte.Add((platz, db));
            _stimme[platz] = db;
        }
    }

    /// <summary>Synthetische Rahmenfolgen: Pegel je 10 ms für Funk und Mikrofon, dazu ein kleines Gate wie das echte.</summary>
    internal static class Synth
    {
        public const float Rauschen = -65f;

        /// <summary>Leistungssumme zweier Pegel in dB.</summary>
        public static float Summe(float a, float b) => 10f * MathF.Log10(MathF.Pow(10f, a / 10f) + MathF.Pow(10f, b / 10f));

        /// <summary>
        /// Hüllkurve zusammenhängender Rede auf <paramref name="pegel"/>: Silben von 80 bis 250 ms mit eigenem Pegel (Streuung 4 dB), dazwischen
        /// Lücken von 20 bis 60 ms 15 dB tiefer. Unregelmäßig wie Sprache, damit zwei unabhängige Sprecher nicht im Takt laufen.
        /// </summary>
        public static float[] Rede(int rahmen, Random r, float pegel)
        {
            var x = new float[rahmen];
            var f = 0;
            while (f < rahmen)
            {
                var laenge = 8 + r.Next(18);
                var hoehe = pegel + (float)(4 * (r.NextDouble() + r.NextDouble() - 1));
                for (var i = 0; i < laenge && f < rahmen; i++, f++)
                    x[f] = hoehe + (float)(5 * Math.Sin(Math.PI * (i + 0.5) / laenge) - 3) + (float)(r.NextDouble() - 0.5);
                var luecke = 2 + r.Next(5);
                for (var i = 0; i < luecke && f < rahmen; i++, f++) x[f] = pegel - 15f + (float)(r.NextDouble() - 0.5);
            }
            return x;
        }

        internal sealed record Szene(float[] FunkDb, bool[] FunkAnfang, int[] FunkPlatz, float[] EigenDb, float[] P, float[] SpielDb, bool[] SpielSprachartig, bool[] EigeneRede);

        /// <summary>
        /// Gespräch: <paramref name="sprecher"/> Funker reden unabhängig (Sprüche 1 bis 3 s, Pausen so, dass bei 4 Sprechern etwa 30 %,
        /// bei 25 etwa 80 % der Zeit Funk läuft), die eigene Stimme redet <paramref name="eigeneAnteil"/> der Zeit, ohne auf den Funk zu achten
        /// (auch über die anderen). Mit <paramref name="echoKopplungDb"/> kommt der Funk 80 ms später um so viel gedämpft ins Mikrofon.
        /// </summary>
        public static Szene Gespraech(int sprecher, int sekunden, double eigeneAnteil, int saat, float? echoKopplungDb = null, float eigenPegelDb = -26f)
        {
            var n = sekunden * 100;
            var r = new Random(saat);
            var leistung = new double[n];
            var anfang = new bool[n];
            var platz = new int[n];
            var lautester = new float[n];
            Array.Fill(lautester, -200f);
            Array.Fill(platz, -1);
            var anteil = sprecher <= 4 ? 0.3 : sprecher >= 25 ? 0.8 : 0.55;
            var jeSprecher = 1 - Math.Pow(1 - anteil, 1.0 / sprecher);
            for (var s = 0; s < sprecher; s++)
            {
                var pegel = -28f + (float)(r.NextDouble() * 6 - 3);
                var f = (int)(r.NextDouble() * 2000);
                while (f < n)
                {
                    var laenge = 100 + r.Next(200);
                    var pause = (int)(-Math.Log(1 - r.NextDouble()) * laenge * (1 - jeSprecher) / jeSprecher);
                    var rede = Rede(laenge, r, pegel);
                    if (f < n) anfang[f] = true;
                    for (var i = 0; i < laenge && f + i < n; i++)
                    {
                        leistung[f + i] += Math.Pow(10, rede[i] / 10);
                        if (rede[i] > lautester[f + i])
                        {
                            lautester[f + i] = rede[i];
                            platz[f + i] = s;
                        }
                    }
                    f += laenge + pause;
                }
            }
            var funk = leistung.Select(l => l > 0 ? (float)(10 * Math.Log10(l)) : -90f).ToArray();
            var eigen = new float[n];
            var p = new float[n];
            var rede2 = new bool[n];
            Array.Fill(eigen, Rauschen);
            Array.Fill(p, 0.05f);
            if (eigeneAnteil > 0)
            {
                var f = 50 + r.Next(300);
                while (f < n)
                {
                    var laenge = 60 + r.Next(250);
                    var rede = Rede(laenge, r, eigenPegelDb);
                    for (var i = 0; i < laenge && f + i < n; i++)
                    {
                        eigen[f + i] = Summe(eigen[f + i], rede[i]);
                        p[f + i] = rede[i] > eigenPegelDb - 12 ? 0.95f : 0.4f;
                        rede2[f + i] = true;
                    }
                    f += laenge + (int)(-Math.Log(1 - r.NextDouble()) * laenge * (1 - eigeneAnteil) / eigeneAnteil) + 40;
                }
            }
            if (echoKopplungDb is { } k)
            {
                for (var f = 8; f < n; f++)
                {
                    if (funk[f - 8] < -80) continue;
                    eigen[f] = Summe(eigen[f], funk[f - 8] + k);
                    if (funk[f - 8] + k > -55) p[f] = MathF.Max(p[f], 0.92f);
                }
            }
            var spiel = new float[n];
            Array.Fill(spiel, float.NaN);
            return new Szene(funk, anfang, platz, eigen, p, spiel, new bool[n], rede2);
        }

        /// <summary>Spielton über die Lautsprecher: nicht sprachartig, kommt in Stößen ins Mikrofon und öffnet die Sprachaktivierung.</summary>
        public static Szene Spielphasen(int sekunden, int saat)
        {
            var n = sekunden * 100;
            var r = new Random(saat);
            var funk = Enumerable.Repeat(-90f, n).ToArray();
            var spiel = new float[n];
            var eigen = new float[n];
            var p = new float[n];
            for (var f = 0; f < n; f++)
            {
                var stoss = f % 400 < 120;
                spiel[f] = stoss ? -20f : -34f;
                eigen[f] = stoss ? -36f + (float)r.NextDouble() * 3 : Rauschen;
                p[f] = stoss ? (r.NextDouble() < 0.05 ? 0.92f : 0.6f) : 0.05f;
            }
            return new Szene(funk, new bool[n], Enumerable.Repeat(-1, n).ToArray(), eigen, p, spiel, new bool[n], new bool[n]);
        }

        public static Szene Stille(int sekunden)
        {
            var n = sekunden * 100;
            var spiel = new float[n];
            Array.Fill(spiel, float.NaN);
            return new Szene(Enumerable.Repeat(-90f, n).ToArray(), new bool[n], Enumerable.Repeat(-1, n).ToArray(), Enumerable.Repeat(Rauschen, n).ToArray(),
                Enumerable.Repeat(0.02f, n).ToArray(), spiel, new bool[n], new bool[n]);
        }

        /// <summary>Spielt Szenen Rahmen für Rahmen in einen Wächter; ein kleines Gate (Linie -50 dBFS, 4 Rahmen Bestätigung, 300 ms Nachlauf).</summary>
        internal sealed class Lauf(EigenerWaechter w)
        {
            private GateZustand _gate = GateZustand.Leise;
            private int _bestaetigt;
            private int _nachlauf;

            public EigenerWaechter W => w;

            public float StimmeDb { get; init; } = -26f;

            public int Phasen { get; private set; }

            public DauersendUrteil HoechstesUrteil { get; private set; }

            public void Abspielen(Szene s) => AbspielenBis(s, () => false);

            /// <summary>Bis die Bedingung gilt; Rückgabe: gespielte Rahmen.</summary>
            public int AbspielenBis(Szene s, Func<bool> ende)
            {
                for (var f = 0; f < s.FunkDb.Length; f++)
                {
                    var e = s.EigenDb[f];
                    var ueber = e > -50f && s.P[f] >= 0.5f;
                    switch (_gate)
                    {
                        case GateZustand.Leise:
                            if (ueber)
                            {
                                _gate = GateZustand.Anlauf;
                                _bestaetigt = 1;
                            }
                            break;
                        case GateZustand.Anlauf:
                            if (ueber) _bestaetigt++;
                            else _gate = GateZustand.Leise;
                            if (_bestaetigt >= 4)
                            {
                                _gate = GateZustand.Senden;
                                Phasen++;
                            }
                            break;
                        case GateZustand.Senden:
                            if (!ueber)
                            {
                                _gate = GateZustand.Nachlauf;
                                _nachlauf = 30;
                            }
                            break;
                        case GateZustand.Nachlauf:
                            if (ueber) _gate = GateZustand.Senden;
                            else if (--_nachlauf <= 0) _gate = GateZustand.Leise;
                            break;
                    }
                    w.Rahmen(new WaechterRahmen
                    {
                        EigenDb = e, EingangDb = e, WiedergabeDb = float.NaN, P = s.P[f], Gate = _gate,
                        VaSendet = _gate is GateZustand.Senden or GateZustand.Nachlauf, FunkDb = s.FunkDb[f], FunkAnfang = s.FunkAnfang[f],
                        FunkPlatz = s.FunkPlatz[f], SpielDb = s.SpielDb[f], SpielSprachartig = s.SpielSprachartig[f], SpielLaeuft = !float.IsNaN(s.SpielDb[f]),
                        StimmeDb = StimmeDb,
                    });
                    if (w.Urteil > HoechstesUrteil) HoechstesUrteil = w.Urteil;
                    if (ende()) return f + 1;
                }
                return s.FunkDb.Length;
            }
        }
    }
}
