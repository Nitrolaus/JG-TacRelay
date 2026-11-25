// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

internal static class MischerTests
{
    private static float[] Mischen(Mischer m, int bloecke, int kanaele = 2)
    {
        var aus = new float[bloecke * 480 * kanaele];
        m.Mischen(aus, kanaele);
        return aus;
    }

    private static float Kanal(float[] verschachtelt, int kanal, int frame, int kanaele = 2) => verschachtelt[frame * kanaele + kanal];

    [Test]
    public static void Panorama_ConstantPower_MitteMinusDreiDb()
    {
        var (l, r) = Mischer.PanoramaFaktoren(0f);
        PruefeWert.Nahe(0.70710678, l, 1e-6, "Mitte links");
        PruefeWert.Nahe(0.70710678, r, 1e-6, "Mitte rechts");
        PruefeWert.Nahe(-3.0103, 20 * Math.Log10(l), 1e-3, "Mitte in dB");
        (l, r) = Mischer.PanoramaFaktoren(-1f);
        PruefeWert.Nahe(1, l, 1e-6, "ganz links: links");
        PruefeWert.Nahe(0, r, 1e-6, "ganz links: rechts");
        (l, r) = Mischer.PanoramaFaktoren(1f);
        PruefeWert.Nahe(0, l, 1e-6, "ganz rechts: links");
        PruefeWert.Nahe(1, r, 1e-6, "ganz rechts: rechts");
        for (var p = -1f; p <= 1f; p += 0.125f)
        {
            (l, r) = Mischer.PanoramaFaktoren(p);
            PruefeWert.Nahe(1, l * l + r * r, 1e-6, $"Leistung konstant bei {p}");
        }
    }

    [Test]
    public static void Pegel_FunkerUndNetz_InDb()
    {
        var m = new Mischer { Klick = null };
        var q = new TestQuelle(0.1f) { Gleichwert = true };
        var platz = m.QuelleHinzufuegen(q, netz: 3, funkerPegelDb: -6f);
        m.SetzeNetzLautstaerke(3, 2f);
        m.SetzeNetzPanorama(3, -1f);
        Mischen(m, 3); // Rampen einschwingen lassen
        var aus = Mischen(m, 1);
        PruefeWert.Nahe(0.1 * Math.Pow(10, -4 / 20.0), Kanal(aus, 0, 240), 1e-6, "links mit -6 dB + 2 dB");
        PruefeWert.Nahe(0, Kanal(aus, 1, 240), 1e-7, "rechts stumm (ganz links)");

        m.SetzeFunkerPegel(platz, 0f);
        m.SetzeNetzPanorama(3, 0f);
        Mischen(m, 1);
        aus = Mischen(m, 1);
        var erwartet = 0.1 * Math.Pow(10, 2 / 20.0) * Math.Sqrt(0.5);
        PruefeWert.Nahe(erwartet, Kanal(aus, 0, 100), 1e-6, "Mitte links");
        PruefeWert.Nahe(erwartet, Kanal(aus, 1, 100), 1e-6, "Mitte rechts");
    }

    [Test]
    public static void Pegelwechsel_WirdGerampt_OhneSprung()
    {
        var m = new Mischer { Klick = null };
        var q = new TestQuelle(0.2f) { Gleichwert = true };
        var platz = m.QuelleHinzufuegen(q, 0);
        Mischen(m, 2);
        m.SetzeFunkerPegel(platz, -20f);
        var aus = Mischen(m, 1);
        var groessterSchritt = 0f;
        for (var i = 1; i < 480; i++) groessterSchritt = MathF.Max(groessterSchritt, MathF.Abs(Kanal(aus, 0, i) - Kanal(aus, 0, i - 1)));
        PruefeWert.Hoechstens(0.2 * 0.7072 / 480 * 1.01, groessterSchritt, "Schritt je Sample während der Rampe");
        PruefeWert.Nahe(0.2 * 0.70710678 * 0.1, Kanal(aus, 0, 479), 1e-5, "am Blockende erreicht");
    }

    [Test]
    public static void Absenkung_WaehrendDurchsage_WeichUndMitHaltezeit()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var netzQuelle = new TestQuelle(0.1f) { Gleichwert = true };
        var durchsage = new TestQuelle(0.1f) { Gleichwert = true, Aktiv = false };
        m.QuelleHinzufuegen(netzQuelle, 0);
        m.QuelleHinzufuegen(durchsage, 1, durchsage: true);

        var verlaufNetz = new List<float>();
        var block = new float[480];
        void Block()
        {
            m.Mischen(block, 1);
            verlaufNetz.Add(m.AbsenkungAktuellDb);
        }
        for (var i = 0; i < 10; i++) Block();
        Pruefe.Gleich(0f, m.AbsenkungAktuellDb, "vor der Durchsage keine Absenkung");

        durchsage.Aktiv = true;
        for (var i = 0; i < 10; i++) Block();
        // 10 ms Verzug, danach in 50 ms auf -12 dB.
        PruefeWert.Nahe(-12, verlaufNetz[15], 0.01, "nach 60 ms voll abgesenkt");
        PruefeWert.Hoechstens(-2, verlaufNetz[11], "nach 20 ms schon unterwegs");
        PruefeWert.Mindestens(-12 + 2, verlaufNetz[12], "aber nicht sprunghaft");
        // Summe: Durchsage 0,1 unberührt plus Netz 0,1 * -12 dB.
        PruefeWert.Nahe(0.1 + 0.1 * Math.Pow(10, -12 / 20.0), block[479], 1e-4, "Durchsage unberührt, Netz abgesenkt");

        durchsage.Aktiv = false;
        for (var i = 0; i < 70; i++) Block();
        // 300 ms Haltezeit, dann in 250 ms zurück.
        PruefeWert.Nahe(-12, verlaufNetz[20 + 29], 0.01, "während der Haltezeit abgesenkt");
        Pruefe.Gleich(0f, verlaufNetz[^1], "danach wieder 0 dB");
        var anstieg = verlaufNetz.Skip(20).ToList();
        for (var i = 1; i < anstieg.Count; i++)
            PruefeWert.Hoechstens(12 * 10 / 250.0 + 1e-4, anstieg[i] - anstieg[i - 1], "Freigabe weich");
    }

    [Test]
    public static void Durchsage_Mindestpegel()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1, DurchsageMindestpegelDb = -20f }) { Klick = null };
        var q = new TestQuelle(0.5f) { Gleichwert = true };
        m.QuelleHinzufuegen(q, 0, funkerPegelDb: -40f, durchsage: true);
        var aus = Mischen(m, 2, 1);
        PruefeWert.Nahe(0.5 * 0.1, aus[^1], 1e-5, "nicht unter -20 dB");
    }

    [Test]
    public static void VieleLauteQuellen_AusgabeNieUeberEins()
    {
        var m = new Mischer();
        var zufall = new Random(11);
        for (var i = 0; i < 16; i++)
            m.QuelleHinzufuegen(new TestQuelle(0.3f + 0.4f * (float)zufall.NextDouble(), 100 + 37 * i), i % 4, funkerPegelDb: 6f);
        for (var n = 0; n < 4; n++) m.SetzeNetzPanorama(n, -1f + n * 0.66f);
        var spitze = 0f;
        var vorBegrenzer = 0f;
        for (var b = 0; b < 300; b++)
        {
            var aus = Mischen(m, 1);
            foreach (var v in aus)
            {
                Pruefe.Wahr(float.IsFinite(v), "endlich");
                spitze = MathF.Max(spitze, MathF.Abs(v));
            }
            vorBegrenzer = MathF.Max(vorBegrenzer, m.SpitzeVorBegrenzer);
        }
        PruefeWert.Mindestens(4, vorBegrenzer, "Summe weit über Vollaussteuerung");
        PruefeWert.Hoechstens(1.0, spitze, "Ausgabe nie über 1,0");
    }

    [Test]
    public static void Sprung_AusStille_AufPlusZwoelfDb_NieUeberEins()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        var q = new TestQuelle(4f, 1000f) { Aktiv = false };
        m.QuelleHinzufuegen(q, 0);
        Mischen(m, 5, 1);
        q.Aktiv = true;
        var spitze = 0f;
        for (var b = 0; b < 50; b++) spitze = MathF.Max(spitze, Pegel.Spitze(Mischen(m, 1, 1)));
        PruefeWert.Hoechstens(1.0, spitze, "Einschwingen ohne Überschwingen über 1,0");
        PruefeWert.Mindestens(0.8, spitze, "Pegel bleibt hoch");
    }

    [Test]
    public static void BeliebigeBlockgroessen_GleichesErgebnis()
    {
        float[] Lauf(int blockgroesse)
        {
            var m = new Mischer { Klick = null };
            m.QuelleHinzufuegen(new TestQuelle(0.9f, 330f), 0, funkerPegelDb: 6f);
            m.QuelleHinzufuegen(new TestQuelle(0.5f, 550f), 1);
            m.SetzeNetzPanorama(1, 0.5f);
            var gesamt = new float[48000 * 2];
            for (var pos = 0; pos < gesamt.Length; pos += blockgroesse * 2)
            {
                var n = Math.Min(blockgroesse * 2, gesamt.Length - pos);
                m.Mischen(gesamt.AsSpan(pos, n), 2);
            }
            return gesamt;
        }
        var a = Lauf(480);
        var b = Lauf(441);
        var c = Lauf(1024);
        Pruefe.Wahr(a.AsSpan().SequenceEqual(b), "441er-Blöcke gleich 480er");
        Pruefe.Wahr(a.AsSpan().SequenceEqual(c), "1024er-Blöcke gleich 480er");
    }

    [Test]
    public static void MehrereGeraete_JeEineInstanz_Unabhaengig()
    {
        var kopfhoerer = new Mischer { Klick = null };
        var lautsprecher = new Mischer(new MischerEinstellungen { Kanaele = 1 }) { Klick = null };
        kopfhoerer.QuelleHinzufuegen(new TestQuelle(0.25f) { Gleichwert = true }, 0);
        lautsprecher.QuelleHinzufuegen(new TestQuelle(0.5f) { Gleichwert = true }, 0, funkerPegelDb: -6.0206f);
        var a = Mischen(kopfhoerer, 2);
        var b = Mischen(lautsprecher, 2, 1);
        PruefeWert.Nahe(0.25 * 0.70710678, a[^1], 1e-6, "Kopfhörer");
        PruefeWert.Nahe(0.25, b[^1], 1e-5, "Lautsprecher");

        // Vierkanalgerät: Kanal 2 und 3 bleiben still.
        var vier = new float[480 * 4];
        kopfhoerer.Mischen(vier, 4);
        Pruefe.Gleich(0f, vier[2], "Kanal 2 still");
        Pruefe.Gleich(0f, vier[^1], "Kanal 3 still");
    }

    [Test]
    public static void Klick_AusQuellenereignis_WirdGemischt()
    {
        var m = new Mischer(new MischerEinstellungen { Kanaele = 1 });
        Pruefe.Wahr(m.Klick is not null, "ab Werk mit Klick");
        // Mit Startwert, damit das Rauschen hier dasselbe ist wie im Vergleich (KlickTests.Rauschen).
        var k = new FunkKlick(saat: 21);
        m.Klick = k;
        var zufall = new Random(21);
        var q = new TestQuelle(0f) { NaechstesEreignis = QuellenEreignis.KlickDruck };
        m.QuelleHinzufuegen(q, 0);
        var aus = Mischen(m, 4, 1);
        // Andere Sprecher: Lautstärke "andere" (seit 0.5.0 einstellbar, A63), mono mittig in voller Höhe.
        PruefeWert.Nahe(Pegel.Spitze(KlickTests.Rauschen(KlickArt.Druck, zufall)) * Pegel.DbZuLinear(k.Einstellungen.AndereDb), Pegel.Spitze(aus), 1e-4,
            "Klick eines anderen Sprechers");

        q.NaechstesEreignis = QuellenEreignis.KlickLoslassen | QuellenEreignis.Sprachaktivierung;
        aus = Mischen(m, 10, 1);
        PruefeWert.Nahe(Pegel.Spitze(KlickTests.Rauschen(KlickArt.Loslassen, zufall)) * Pegel.DbZuLinear(k.Einstellungen.AndereDb - 18f), Pegel.Spitze(aus), 1e-4,
            "bei Sprachaktivierung 18 dB leiser");
    }

    // ---- Funk-Lautstärke (seit 0.10.0, Q74, E7): ein Faktor auf die ganze Summe vor dem Begrenzer ----

    /// <summary>Ein Mischer mit einem gleichbleibenden Funker und der Hörprobe (funk: false), Klick an; je Block die Ausgabe.</summary>
    private static Mischer FunkUndHoerprobe(out FunkSpur spur)
    {
        var m = new Mischer { Klick = null };
        m.QuelleHinzufuegen(new TestQuelle(0.2f) { Gleichwert = true }, 0);
        m.QuelleHinzufuegen(new TestQuelle(0.1f) { Gleichwert = true }, 63, funk: false);
        spur = new FunkSpur();
        m.Spur = spur;
        return m;
    }

    private static float SpurDb(FunkSpur spur)
    {
        Pruefe.Wahr(spur.Lesen(out var db, out _, out _), "die Spur hat Blöcke");
        return db;
    }

    [Test]
    public static void FunkLautstaerke_Hundert_Bitgleich_AuchNachDerRampe()
    {
        var vergleich = FunkUndHoerprobe(out var vergleichSpur);
        var m = FunkUndHoerprobe(out var spur);
        Pruefe.Gleich(1f, m.FunkLautstaerke, "ab Werk 1 (100 %)");
        m.FunkLautstaerke = 1f;
        Pruefe.Wahr(Mischen(vergleich, 5).AsSpan().SequenceEqual(Mischen(m, 5)), "1 gesetzt: bitgleich wie nie gesetzt");
        Pruefe.Gleich(SpurDb(vergleichSpur), SpurDb(spur), "die Spur bitgleich");
        // Auf die Hälfte und zurück: nach der Rampe wieder bitgleich (die Quellen bleiben gleich, nur die Rampe lag dazwischen).
        m.FunkLautstaerke = 0.5f;
        _ = Mischen(m, 30);
        _ = Mischen(vergleich, 30);
        m.FunkLautstaerke = 1f;
        _ = Mischen(m, 30);
        _ = Mischen(vergleich, 30);
        _ = SpurDb(spur);
        _ = SpurDb(vergleichSpur);
        Pruefe.Wahr(Mischen(vergleich, 3).AsSpan().SequenceEqual(Mischen(m, 3)), "zurück auf 1: wieder bitgleich");
        Pruefe.Gleich(SpurDb(vergleichSpur), SpurDb(spur), "die Spur wieder bitgleich");
        m.FunkLautstaerke = float.NaN;
        Pruefe.Gleich(1f, m.FunkLautstaerke, "keine Zahl gilt als 1: ein Fehler macht den Funk nie stumm");
        m.FunkLautstaerke = 7f;
        Pruefe.Gleich(1f, m.FunkLautstaerke, "über 1 begrenzt");
        m.FunkLautstaerke = -1f;
        Pruefe.Gleich(0f, m.FunkLautstaerke, "unter 0 begrenzt");
    }

    [Test]
    public static void FunkLautstaerke_FunkKlickUndHoerprobeGemeinsam_NullIstStumm()
    {
        var voll = FunkUndHoerprobe(out var vollSpur);
        var halb = FunkUndHoerprobe(out var halbSpur);
        halb.FunkLautstaerke = 0.25f; // 50 % am Regler: (50/100)^2, etwa -12 dB
        // Der erste Block beginnt gleich beim Wert, ohne Rampe.
        var a = Mischen(voll, 3);
        var b = Mischen(halb, 3);
        for (var i = 0; i < a.Length; i++) PruefeWert.Nahe(a[i] * 0.25, b[i], 1e-6, "Funk und Hörprobe zusammen auf ein Viertel, Sample " + i);
        PruefeWert.Nahe(SpurDb(vollSpur) - 12.0412, SpurDb(halbSpur), 1e-3, "die Spur des Wächters rechnet den Faktor mit (-12 dB)");
        PruefeWert.Nahe(voll.SpitzeVorBegrenzer * 0.25, halb.SpitzeVorBegrenzer, 1e-6, "vor dem Begrenzer");

        // Klick dazu: auch er wird leiser, ganz links stumm.
        voll.Klick = new FunkKlick(saat: 5);
        halb.Klick = new FunkKlick(saat: 5);
        voll.Klick.Anfordern(KlickArt.Druck);
        halb.Klick.Anfordern(KlickArt.Druck);
        a = Mischen(voll, 4);
        b = Mischen(halb, 4);
        for (var i = 0; i < a.Length; i++) PruefeWert.Nahe(a[i] * 0.25, b[i], 1e-5, "mit Klick auf ein Viertel, Sample " + i);

        // Ganz links: nach der Rampe (und dem Vorlauf des Begrenzers) stumm, auch ein neuer Klick; die Spur meldet nichts mehr.
        halb.FunkLautstaerke = 0f;
        _ = Mischen(halb, 10);
        _ = SpurDb(halbSpur);
        halb.Klick.Anfordern(KlickArt.Loslassen);
        var still = Mischen(halb, 5);
        Pruefe.Gleich(0f, Pegel.Spitze(still), "0 %: stumm, Funk, Hörprobe und Klick");
        PruefeWert.Hoechstens(Pegel.MinDbfs, SpurDb(halbSpur), "die Spur meldet nichts mehr");
    }

    [Test]
    public static void FunkLautstaerke_Rampe_OhneKnacken()
    {
        var m = new Mischer { Klick = null };
        // Ohne Pegelausgleich: Er gliche den gleichbleibenden Funker sonst nebenher ein wenig an, und die Rampe wäre nicht allein zu messen.
        m.Waechter.AngleichungAn = false;
        m.QuelleHinzufuegen(new TestQuelle(0.2f) { Gleichwert = true }, 0);
        Mischen(m, 3);
        var voll = 0.2 * 0.70710678;
        m.FunkLautstaerke = 0f;
        var verlauf = new List<float>();
        for (var b = 0; b < 25; b++)
        {
            var aus = Mischen(m, 1);
            for (var i = 0; i < 480; i++) verlauf.Add(Kanal(aus, 0, i));
        }
        var groessterSchritt = 0.0;
        for (var i = 1; i < verlauf.Count; i++) groessterSchritt = Math.Max(groessterSchritt, Math.Abs(verlauf[i] - verlauf[i - 1]));
        // Fünf Prozent Spiel für den Begrenzer hinter der Rampe (gemessen 3,5 %); ein Knacken wäre um Größenordnungen größer.
        PruefeWert.Hoechstens(voll * Mischer.FunkLautstaerkeSchritt / 480 * 1.05, groessterSchritt, "Schritt je Sample: kein Sprung, kein Knacken");
        PruefeWert.Mindestens(voll * (1 - Mischer.FunkLautstaerkeSchritt) - 1e-5, verlauf[479], "nach einem Block noch fast voll");
        PruefeWert.Nahe(0, verlauf[^1], 1e-7, "nach 200 ms stumm");
        PruefeWert.Mindestens(1e-4, verlauf[(19 * 480) - 1], "nicht früher");
    }

    [Test]
    public static void Echtzeit_Mischen_AllokiertNicht()
    {
        var m = new Mischer();
        for (var i = 0; i < 8; i++) m.QuelleHinzufuegen(new TestQuelle(0.3f, 200 + 50 * i) { NaechstesEreignis = QuellenEreignis.KlickDruck }, i % 3, durchsage: i == 0);
        m.Klick!.Anfordern(KlickArt.Loslassen);
        var aus = new float[441 * 2];
        for (var i = 0; i < 50; i++) m.Mischen(aus, 2);
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 2000; i++) m.Mischen(aus, 2);
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in Mischen");
    }
}
